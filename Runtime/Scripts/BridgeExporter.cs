using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Pencil_4;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pencil4_Bridge
{
    public static class Exporter
    {
        public static void ExportNode(string savePath, LineListNode lineListNode)
        {
            RegisterExporters();

            WriteRenderPriority(lineListNode);

            var exportTargets = new HashSet<NodeBase> ();
            CollectNodeBase(lineListNode, exportTargets);

            var serializationCallbacks = exportTargets
                .Where(x => x.GetComponent(typeof(ISerializationCallbackReceiver)) != null)
                .Select(x => x.GetComponent(typeof(ISerializationCallbackReceiver)) as ISerializationCallbackReceiver);
            foreach(var callback in serializationCallbacks)
            {
                callback.OnBeforeSerialize();
            }

            var nodes = exportTargets
                .Select(x => new
                {
                    nodeType = x.JsonNodeType(),
                    name = x.name,
                    id = x.gameObject.GetInstanceID().ToString(),
                    jsonData = JsonMapper.ToObject(JsonMapper.ToJson(x))
                });
    
            var rootDict = JsonMapper.ToObject("{}") as IDictionary;
            rootDict.Add(KeyName.Platform, "Unity " + UnityEngine.Application.unityVersion);
            rootDict.Add(KeyName.JsonFileVersion, FileVersion.ToVersionString());
            rootDict.Add(KeyName.ScaleFactor, 1.0); // Unityの単位系はメートルで固定
    
            var lineNodes = JsonMapper.ToObject("{}") as IDictionary;
            var materialNodes = JsonMapper.ToObject("{}") as IDictionary;

            // 同名のマテリアルがシーン内に複数ある場合、最初に見つかったマテリアルを利用する
            var materialNameToIds = Utils.EnumerateMaterials()
                .GroupBy(m => m.name) 
                .ToDictionary(ms => ms.First().name, ms => ms.First().GetInstanceID().ToString());
            
            foreach(var nodeData in nodes)
            {
                var nodeDict = JsonMapper.ToObject("{}") as IDictionary;

                if (nodeData.nodeType == Utils.GetJsonAlias(typeof(MaterialLineFunctionsNode)))
                {
                    nodeDict.Add(KeyName.NodeType, nodeData.nodeType);
                    nodeDict.Add(KeyName.NodeName, nodeData.name);
                    var nodeParams = nodeData.jsonData;
                    var targetMaterialNames = nodeParams["TargetMaterials"];
                    for (var i = 0; i < targetMaterialNames.Count; i++)
                    {
                        var materialName = (string)targetMaterialNames[i]["Name"];
                        var dummyMaterial = CreateDummyPencilMaterialData(materialName, nodeData.id);
                        materialNodes.Add(materialNameToIds[materialName], dummyMaterial);
                    }

                    var paramsDict = nodeData.jsonData as IDictionary;
                    paramsDict.Remove("TargetMaterials");
                    nodeDict.Add(KeyName.NodeParams, paramsDict);
                    materialNodes.Add(nodeData.id, nodeDict);
                }
                else
                {
                    nodeDict.Add(KeyName.NodeType, nodeData.nodeType);
                    nodeDict.Add(KeyName.NodeName, nodeData.name);
                    nodeDict.Add(KeyName.NodeParams, nodeData.jsonData);
                    lineNodes.Add(nodeData.id, nodeDict);
                }
            }
    
            rootDict.Add(KeyName.LineNode, lineNodes);
            rootDict.Add(KeyName.MaterialNode, materialNodes);
    
            var jsonWriter = new JsonWriter {PrettyPrint = true};
            ((JsonData) rootDict).ToJson(jsonWriter);
            var outJson = jsonWriter.ToString();
    
            using (var writer = new StreamWriter(savePath, false))
            {
                writer.Write(outJson);
                writer.Flush();
            }
        }
        
        
        private static void WriteRenderPriority(LineListNode lineListNode)
        {
            var idx = 0;
            foreach (var lineObj in lineListNode.LineList)
            {
                var line = lineObj.GetComponent<LineNode>();
                if (line != null)
                {
                    line.BRIDGE_RenderPriority = idx;
                }
                idx++;
            }
        }
        
        private static IDictionary CreateDummyPencilMaterialData(string materialName, string lineFunctionsId)
        {
            var dummyMaterial = new PencilMaterialNodeDummy();
            var materialParams = JsonMapper.ToObject(JsonMapper.ToJson(dummyMaterial));
            materialParams["LineFunctions"] = lineFunctionsId;
            var nodeDict = JsonMapper.ToObject("{}") as IDictionary;
            nodeDict.Add(KeyName.NodeType, "PencilMaterial");
            nodeDict.Add(KeyName.NodeName, materialName);
            nodeDict.Add(KeyName.NodeParams, materialParams);
            return nodeDict;
        }
        
        private static IEnumerable<Vector2> CalcUniversalCurve(AnimationCurve curve)
        {
            Action<List<Vector2>, Vector2, Vector2> div = null;
            div = (points, leftPoint, rightPoint) =>
            {
                const double threshold = 0.05;
                if (rightPoint.x - leftPoint.x < threshold)
                {
                    return;
                }

                var interpolated1 = Vector2.Lerp(leftPoint, rightPoint, 1.0f / 3.0f);
                var interpolated2 = Vector2.Lerp(leftPoint, rightPoint, 2.0f / 3.0f);
                var actualY1 = curve.Evaluate(interpolated1.x);
                var actualY2 = curve.Evaluate(interpolated2.x);

                if (!(threshold < Math.Abs(interpolated1.y - actualY1))
                    && !(threshold < Math.Abs(interpolated2.y - actualY2)))
                {
                    return;
                }

                var newPointX = (leftPoint.x + rightPoint.x) * 0.5f;
                var newPointY = curve.Evaluate(newPointX);
                var newPoint = new Vector2(newPointX, newPointY);
                points.Add(newPoint);
                div(points, leftPoint, newPoint);
                div(points, newPoint, rightPoint);
            };

            var existingPoints = curve.keys.Select(k => new Vector2(k.time, k.value)).ToList();
            var newPoints = new List<Vector2> { };

            using (var leftPoints = existingPoints.GetEnumerator())
            using (var rightPoints = existingPoints.Skip(1).GetEnumerator())
            {
                while (rightPoints.MoveNext())
                {
                    leftPoints.MoveNext();
                    div(newPoints, leftPoints.Current, rightPoints.Current);
                }
            }

            return existingPoints.Concat(newPoints).OrderBy(p => p.x);
        }

        private static void CollectNodeBase(NodeBase node, HashSet<NodeBase> nodes)
        {
            if (node == null || nodes.Contains(node))
            {
                return;
            }

            nodes.Add(node);

            foreach (FieldInfo f_info in node.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f_info.GetCustomAttributes(typeof(JsonDataMember), false).Length == 0 ||
                    f_info.GetCustomAttributes(typeof(PencilNode), false).Length == 0)
                {
                    continue;
                }

                if (f_info.FieldType == typeof(GameObject) && (f_info.GetValue(node) as GameObject) != null)
                {
                    CollectNodeBase((f_info.GetValue(node) as GameObject).GetComponent<NodeBase>(), nodes);
                }
                else if (f_info.FieldType == typeof(List<GameObject>))
                {
                    foreach (object elem in (f_info.GetValue(node) as List<GameObject>).Where(x => x as GameObject != null))
                    {
                        CollectNodeBase((elem as GameObject).GetComponent<NodeBase>(), nodes);
                    }
                }
            }
        }


        #region Exporter
        
        private static void RegisterExporters()
        {
            JsonMapper.RegisterExporter<GameObject>(ExportGameObject);
            JsonMapper.RegisterExporter<BrushDetailNode.LoopDirection>(ExportLoopDirection);
            JsonMapper.RegisterExporter<AnimationCurve>(ExportAnimationCurve);
            JsonMapper.RegisterExporter<float>(ExportFloat);
            JsonMapper.RegisterExporter<Color>(ExportColor);
            JsonMapper.RegisterExporter<Vector2>(ExportVector2);
            JsonMapper.RegisterExporter<Material>(ExportMaterial);
            JsonMapper.RegisterExporter<Texture2D>(ExportTexture2D);
        }
        
        private static void ExportGameObject(GameObject value, JsonWriter writer)
        {
            if (value == null)
            {
                writer.Write(null);
                return;
            }

            if (value.GetComponent<NodeBase>() != null)
            {
                writer.Write(value.GetInstanceID().ToString());
                return;
            }

            writer.Write(value.name);
        }

        private static void ExportLoopDirection(BrushDetailNode.LoopDirection value, JsonWriter writer)
        {
            switch (value)
            {
                case BrushDetailNode.LoopDirection.Left:
                    writer.Write(1); 
                    break;
                case BrushDetailNode.LoopDirection.Right:
                    writer.Write(0);
                    break;
            }
        }

        private static void ExportAnimationCurve(AnimationCurve value, JsonWriter writer)
        {
            writer.WriteObjectStart();
#if UNITY_EDITOR
                // UnityCurveKeysの書き込み
                writer.WritePropertyName(KeyName.UnityCurveKeys);
                writer.WriteArrayStart();

                var index = 0;
                foreach (var keyFrame in value.keys)
                {
                    
                    writer.WriteObjectStart();
                    writer.WritePropertyName(KeyName.CurveTime);
                    writer.Write(Convert.ToDouble(keyFrame.time));
                    writer.WritePropertyName(KeyName.CurveValue);
                    writer.Write(Convert.ToDouble(keyFrame.value));
                    writer.WritePropertyName(KeyName.CurveLeftTangentMode);
                    writer.Write((int) AnimationUtility.GetKeyLeftTangentMode(value, index));
                    writer.WritePropertyName(KeyName.CurveRightTangentMode);
                    writer.Write((int) AnimationUtility.GetKeyRightTangentMode(value, index));
                    writer.WritePropertyName(KeyName.CurveKeyBroken);
                    writer.Write(AnimationUtility.GetKeyBroken(value, index));
                    writer.WritePropertyName(KeyName.CurveInTangent);
                    writer.Write(Convert.ToDouble(keyFrame.inTangent));
                    writer.WritePropertyName(KeyName.CurveOutTangent);
                    writer.Write(Convert.ToDouble(keyFrame.outTangent));
#if UNITY_2018_1_OR_NEWER
                    writer.WritePropertyName(KeyName.CurveInWeight);
                    writer.Write(Convert.ToDouble(keyFrame.inWeight));
                    writer.WritePropertyName(KeyName.CurveOutWeight);
                    writer.Write(Convert.ToDouble(keyFrame.outWeight));
                    writer.WritePropertyName(KeyName.CurveWeightedMode);
                    writer.Write((int) keyFrame.weightedMode);
#endif
                    writer.WriteObjectEnd();
                    index++;
                }

                writer.WriteArrayEnd();
#endif

                // UniversalKeysの書き込み
                writer.WritePropertyName(KeyName.UniversalCurveKeys);
                writer.WriteArrayStart();
                foreach (var point in CalcUniversalCurve(value))
                {
                    writer.WriteArrayStart();
                    writer.Write(point.x);
                    writer.Write(point.y);
                    writer.WriteArrayEnd();
                }

                writer.WriteArrayEnd();

                writer.WriteObjectEnd();
        }

        private static void ExportFloat(float value, JsonWriter writer)
        {
            writer.Write(Convert.ToDouble(value));
        }


        private static void ExportColor(Color value, JsonWriter writer)
        {
            writer.WriteArrayStart();
            writer.Write(value.r);
            writer.Write(value.g);
            writer.Write(value.b);
            writer.Write(value.a);
            writer.WriteArrayEnd();
        }

        private static void ExportVector2(Vector2 value, JsonWriter writer)
        {
            writer.WriteArrayStart();
            writer.Write(Convert.ToDouble(value.x));
            writer.Write(Convert.ToDouble(value.y));
            writer.WriteArrayEnd();
        }

        private static void ExportMaterial(Material value, JsonWriter writer)
        {
            writer.WriteObjectStart();
            writer.WritePropertyName("Name");
            writer.Write(value.name);
            writer.WritePropertyName("Id");
            writer.Write(null);
            writer.WritePropertyName("MaterialType");
            writer.Write("Other");
            writer.WriteObjectEnd();
        }

        private static void ExportTexture2D(Texture2D value, JsonWriter writer)
        {
            writer.Write(value.name);
        }
        
        #endregion
    }
}