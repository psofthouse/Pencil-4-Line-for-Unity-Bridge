using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Pencil_4;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pencil4_Bridge
{
    public class ImportOptionsModel
    {
        public enum ImportOptions { Replace, Merge }

        public class LineData
        {
            public bool ShouldImport;
            public string NodeId;
            public string NodeName;
        }

        public List<LineData> PencilLines = new List<LineData>();
        
        public ImportOptions ImportOption = ImportOptions.Replace;

        public bool IsUnitConversionAuto = true;

        public float ScaleFactor = 1.0f;

        public bool ShouldImportDisabledSpecificBrush = false;

        public bool ShouldImportDisabledReductionSettings = false;
    }
    
    public static class Importer
    {
        private class CustomImporterArgs
        {
            public Component node;
            public JsonData value;
            public FieldInfo field;
            public float scale;
            public Func<string, GameObject> findNodeFunc;

            public void SetValueByReflection(object val)
            {
                field.SetValue(node, val);
            }
        }

        private class LineListNodeToMerge
        {
            public List<GameObject> LineList = new List<GameObject>();
            public List<GameObject> LineFunctionsList = new List<GameObject>();
            public List<Material> DoubleSidedMaterials = new List<Material>();
            public List<GameObject> IgnoreObjectList = new List<GameObject>();
            public List<GameObject> LineGroupList = new List<GameObject>();
        }
        
        // フィールドの型に対応するインポータの辞書
        static Dictionary<Type, Action<CustomImporterArgs>> _customImporter = new Dictionary<Type, Action<CustomImporterArgs>>();
        
        // アセットを探すデリゲート
        // (AssetDatabaseクラスが実行環境によっては使えないため、
        //  Init関数で外から渡されてくる事を想定する)
        static Func<string, string, UnityEngine.Object> _assetFinder = (filter, name) => null;

        private static void DestroyGameObject(GameObject o)
        {
#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabInstance(o))
            {
                Undo.RecordObject(o, "Pencil+ Bridge Import");
                o.SetActive(false);
            }
            else
            {
                Undo.DestroyObjectImmediate(o);
            }
#else
            o.transform.parent = null;
            Object.DestroyImmediate(o);
#endif
        }


        public static void ImportNodeWithOptions(
            JsonData jsonData, ImportOptionsModel options, LineListNode lineListNode)
        {   
            var existingMaterials = Utils.EnumerateMaterials().ToList();
            
            var jsonScale = jsonData.HasDoubleKey(KeyName.ScaleFactor)
                ? (float)(double)jsonData[KeyName.ScaleFactor]
                : 1.0f;

            var importFromUnity = jsonData.TryGetStringValue(KeyName.Platform) != null
                ? jsonData.TryGetStringValue(KeyName.Platform).StartsWith("Unity")
                : false;

            var lineNodeIdsToImport = CollectLineRelatedNodeIds(
                jsonData,
                options.PencilLines.Where(x => x.ShouldImport).Select(x => x.NodeId).ToList(),
                options.ShouldImportDisabledSpecificBrush,
                options.ShouldImportDisabledReductionSettings);
            var texmapNodeIds = lineNodeIdsToImport
                .SelectMany(x => CollectTexmapNodeIds(jsonData, x));

            var unityNodeTypeDic = Assembly.GetAssembly(typeof(NodeBase)).GetTypes()
                .Where(t => t.IsSubclassOf(typeof(NodeBase)) && !t.IsAbstract)
                .ToDictionary(Utils.GetJsonAlias, t => t);
            
            var mustImportNodeTypes = new HashSet<string>{"LineList", "LineGroup"};

            var lineNodesData = jsonData[KeyName.LineNode]
                .Cast<KeyValuePair<string, JsonData>>()
                .Where(x => mustImportNodeTypes.Contains((string)x.Value[KeyName.NodeType])
                            || lineNodeIdsToImport.Contains(x.Key)
                            || texmapNodeIds.Contains(x.Key)).ToList();
            var materialNodesData = jsonData[KeyName.MaterialNode].Cast<KeyValuePair<string, JsonData>>().ToList();

            var nodesInfoToImport = lineNodesData.Concat(materialNodesData)
                .Where(pair => (string)pair.Value[KeyName.NodeType] != Utils.GetJsonAlias(typeof(PencilMaterialNodeDummy)))
                .Where(pair => unityNodeTypeDic.ContainsKey((string)pair.Value[KeyName.NodeType]))
                .Select(pair =>
                {
                    var id = pair.Key;
                    var unityNodeType = unityNodeTypeDic[(string)pair.Value[KeyName.NodeType]];
                    var node = unityNodeType == typeof(LineListNode)
                        ? lineListNode.gameObject
                        : NodeBase.CreateNodeObjectFromType(unityNodeType);
#if UNITY_EDITOR
                    if (unityNodeType == typeof(LineListNode))
                    {
                        // ラインセットの状態を記録する
                        Undo.RegisterCompleteObjectUndo(node.GetComponent<NodeBase>(), "Pencil+ Bridge Import");
                    }
                    else
                    {
                        // 新規生成されたオブジェクトにRecordObject()を実行するとシリアライザが走ってしまい、
                        // TextureMapNode()の読み込み処理が適切に行われない。
                        // そのため、RegisterCreatedObjectUndo()のみを実行する。
                        Undo.RegisterCreatedObjectUndo(node, "Pencil+ Bridge Import");
                    }
#endif
                    var nodeParams = pair.Value[KeyName.NodeParams];
                    node.name = (string)pair.Value[KeyName.NodeName];
                    return new {id = id, node = node, nodeParams = nodeParams, type = unityNodeType};
                })
                .ToList();

            var pencilMaterialDummies = materialNodesData
                .Where(pair => pair.Value.HasKeyAndValue(KeyName.NodeType)
                               && (string)pair.Value[KeyName.NodeType] == Utils.GetJsonAlias(typeof(PencilMaterialNodeDummy)))
                .Select(pair => new
                {
                    id = pair.Key,
                    nodeParams = pair.Value[KeyName.NodeParams],
                    name = EscapeMaterialName((string)pair.Value[KeyName.NodeName])
                })
                .ToList();


            var lineNodeNamesToOverwrite =
                new HashSet<string>(nodesInfoToImport.Where(x => x.type == typeof(LineNode)).Select(x => x.node.name));

            var lineGroupNamesToOverwrite =
                new HashSet<string>(nodesInfoToImport.Where(x => x.type == typeof(LineGroupNode))
                    .Select(x => x.node.name));


            // 上書きインポートモードの場合、上書き対象のLineNode, LineFunctionsNode, LineGroupNodeを削除
            var lineListToMerge = new LineListNodeToMerge();
            if (options.ImportOption == ImportOptionsModel.ImportOptions.Replace)
            {
                // LineNode
                var lineNodes = lineListNode.LineList
                    .GroupBy(x => lineNodeNamesToOverwrite.Contains(x.gameObject.name))
                    .ToList();
                var linesToDelete = lineNodes.Where(x => x.Key).SelectMany(x => x);
                foreach (var line in linesToDelete)
                {
                    DestroyGameObject(line);
                }

                lineListToMerge.LineList = lineNodes.Where(x => !x.Key).SelectMany(x => x).ToList();
                
                
                // LineFunctionsNode
                var materialsToOverwrite = existingMaterials.Where(x => pencilMaterialDummies.Select(y => y.name).Contains(x.name)).ToList();
                foreach(var lineFunc in lineListNode.LineFunctionsList.Where(x => x).Select(x => x.GetComponent<MaterialLineFunctionsNode>()))
                {
                    if (lineFunc.TargetMaterials.Count == 0) continue;
                    
                    lineFunc.TargetMaterials = lineFunc.TargetMaterials.Except(materialsToOverwrite).ToList();
                    if (lineFunc.TargetMaterials.Count > 0) continue;

                    DestroyGameObject(lineFunc.gameObject);
                }

                lineListToMerge.LineFunctionsList = lineListNode.LineFunctionsList.Where(x => x != null).ToList(); 
                
                
                // LineGroupNode
                var lineGroups = lineListNode.LineGroupList
                    .Where(x => x)
                    .GroupBy(x => lineGroupNamesToOverwrite.Contains(x.gameObject.name))
                    .ToList();
                var lineGroupsToDelete = lineGroups.Where(x => x.Key).SelectMany(x => x);
                foreach (var group in lineGroupsToDelete)
                {
                    DestroyGameObject(group);
                }

                lineListToMerge.LineGroupList = lineGroups.Where(x => !x.Key).SelectMany(x => x).ToList();
            }
            else
            {
                lineListToMerge.LineList = new List<GameObject>(lineListNode.LineList);
                lineListToMerge.LineFunctionsList = new List<GameObject>(lineListNode.LineFunctionsList);
                lineListToMerge.LineGroupList = new List<GameObject>(lineListNode.LineGroupList);
            }
            
            // DoubleSidedMaterialsとIgnoreObjectListは無条件にマージする
            lineListToMerge.DoubleSidedMaterials = new List<Material>(lineListNode.DoubleSidedMaterials);
            lineListToMerge.IgnoreObjectList = new List<GameObject>(lineListNode.IgnoreObjectList);
            
            lineListNode.LineList.Clear();
            lineListNode.LineFunctionsList.Clear();
            lineListNode.DoubleSidedMaterials.Clear();
            lineListNode.IgnoreObjectList.Clear();
            lineListNode.LineGroupList.Clear();

            
            foreach (var nodeInfo in nodesInfoToImport)
            {
                var nodeComponent = nodeInfo.node.GetComponent(nodeInfo.type);
                var nodeParams = nodeInfo.nodeParams;
                var nodeParamsDict = nodeInfo.nodeParams as IDictionary;

                var fields = nodeInfo.type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ToDictionary(field =>
                {
                    var aliases = field.GetCustomAttributes(typeof(JsonAlias), false);
                    return aliases.Length > 0 ? ((JsonAlias)aliases[0]).Alias : field.Name;
                });

                foreach (string paramName in nodeParamsDict.Keys)
                {
                    // paramNameはJSON上のパラメータ名(NameAliasが反映された物)
                    if (!nodeParams.HasKeyAndValue(paramName) || !fields.ContainsKey(paramName))
                    {
                        continue;
                    }

                    var field = fields[paramName];
                    var value = nodeParams[paramName];

                    try
                    {
                        ImportOneParam(
                            nodeComponent,
                            value,
                            field,
                            options.IsUnitConversionAuto ? jsonScale : options.ScaleFactor,
                            x =>
                            {
                                var n = nodesInfoToImport.Find(y => y.id == x);
                                return n != null ? n.node : null;
                            });
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is InvalidCastException)
                            && !(ex is IndexOutOfRangeException)
                            && !(ex is JsonException))
                        {
                            throw;
                        }

                        Debug.Log(string.Format("Couldn't deserialize {0}.{1}", nodeComponent.name, field.Name));
                    }
                }


                var lineFunctionsNodeComponent = nodeComponent as MaterialLineFunctionsNode;
                if (lineFunctionsNodeComponent != null)
                {
                    foreach (var _pencilMaterialDummyData in pencilMaterialDummies)
                    {
                        var pencilMaterialDummyData = _pencilMaterialDummyData;
                        var pencilMaterialParams = pencilMaterialDummyData.nodeParams;
                        if (!pencilMaterialParams.HasKeyAndValue("LineFunctions"))
                        {
                            continue;
                        }

                        var lineFunctionsIdData = pencilMaterialParams["LineFunctions"];
                        if (lineFunctionsIdData == null || !lineFunctionsIdData.IsString)
                        {
                            continue;
                        }

                        if ((string)lineFunctionsIdData != nodeInfo.id)
                        {
                            continue;
                        }

                        var materialsToConnect =
                            existingMaterials.Where(x => x.name == pencilMaterialDummyData.name);
                        lineFunctionsNodeComponent.TargetMaterials.AddRange(materialsToConnect);

                    }
                }

                // Unity以外のプラットフォームでエクスポートされたファイルを処理するとき、
                // TexureMapNodeのデシリアライズ時のチェックは不要なので、フラグを設定する
                if (!importFromUnity)
                {
                    var textureMapNode = nodeComponent as TextureMapNode;
                    if (textureMapNode != null && fields.ContainsKey("_skipFlipScreenV_AfterDeserialize"))
                    {
                        fields["_skipFlipScreenV_AfterDeserialize"].SetValue(textureMapNode, true);
                    }
                }

                // デシリアライズ後のコールバックを呼ぶ
                if (nodeComponent.GetComponent(typeof(ISerializationCallbackReceiver)) != null)
                {
                    (nodeComponent.GetComponent(typeof(ISerializationCallbackReceiver)) as ISerializationCallbackReceiver).OnAfterDeserialize();
                }
            }

            
            // LineListNodeがJSONの中に無い場合は、LineListNodeのフィールドを復元
            // (他プラットフォームでエクスポートされたJSON用の処理)
            if (nodesInfoToImport.All(x => x.type != typeof(LineListNode)))
            {

                var newLines = nodesInfoToImport
                    .Where(x => x.type == typeof(LineNode))
                    .Select(x => x.node)
                    .ToList();

                lineListNode.LineList.AddRange(newLines);

                foreach (var lineNode in newLines)
                {
                    lineNode.transform.parent = lineListNode.gameObject.transform;
                }

                var newLineFunctions = nodesInfoToImport
                    .Where(x => x.type == typeof(MaterialLineFunctionsNode))
                    .Select(x => x.node)
                    .ToList();

                lineListNode.LineFunctionsList.AddRange(newLineFunctions);

                foreach (var lineFunctions in newLineFunctions)
                {
                    lineFunctions.transform.parent = lineListNode.gameObject.transform;
                }
            }

            var existingLineListNum = lineListToMerge.LineList.Count;
            var newLineListNum = lineListNode.LineList.Count;
            lineListNode.LineList = lineListToMerge.LineList
                .Concat(lineListNode.LineList.OrderBy(x => x.GetComponent<LineNode>().BRIDGE_RenderPriority)).ToList();

            // ヒエラルキー内のノードの順番をLineListの順番に合わせる
            foreach (var i in Enumerable.Range(existingLineListNum, newLineListNum))
            {
                lineListNode.LineList[i].transform.SetSiblingIndex(i);
            }
            
            lineListNode.LineFunctionsList = lineListToMerge.LineFunctionsList
                .Concat(lineListNode.LineFunctionsList).ToList();
            lineListNode.DoubleSidedMaterials = lineListToMerge.DoubleSidedMaterials
                .Concat(lineListNode.DoubleSidedMaterials).Distinct().ToList();
            lineListNode.IgnoreObjectList = lineListToMerge.IgnoreObjectList
                .Concat(lineListNode.IgnoreObjectList).Distinct().ToList();
            lineListNode.LineGroupList = lineListToMerge.LineGroupList
                .Concat(lineListNode.LineGroupList).ToList();
        }

#region Utility Functions

        /// <summary>
        /// マテリアル名に使えない文字をエスケープする
        /// (Unityのマテリアルはアセットとして扱われるので、Windowsのファイル名の制限を受ける)
        /// </summary>
        /// <param name="unescapedName">未エスケープのマテリアル名</param>
        /// <returns>エスケープされたマテリアル名</returns>
        private static string EscapeMaterialName(string unescapedName)
        {
            var regex = new Regex(@"[\/\?\<\>\\\:\*\|""]");
            return regex.Replace(unescapedName, "_");
        }

        private static bool HasAttribute<T>(this FieldInfo field)
        {
            return field.GetCustomAttributes(typeof(T), false).Length > 0;
        }
        
        private static IEnumerable<string> CollectLineRelatedNodeIds(
            JsonData source,
            ICollection<string> lineNodeIds,
            bool shouldImportDisabledSpecificBrush,
            bool shouldImportDisabledReduction)
        {
            if (!source.IsObject 
                || !source.HasKeyAndValue(KeyName.LineNode)
                || !source[KeyName.LineNode].IsObject)
            {
                return new List<string>();
            }

            var lineNodes = source[KeyName.LineNode];

            var relatedLineSetIds = lineNodes.Keys
                .Where(lineNodeIds.Contains)
                .Select(key => source[KeyName.LineNode][key])
                .Where(line => line.HasKeyAndValue(KeyName.NodeParams))
                .Select(line => line[KeyName.NodeParams])
                .Where(lineParams => lineParams.HasKeyAndValue(KeyName.LineSets))
                .Where(lineParams => lineParams[KeyName.LineSets].IsArray)
                .SelectMany(lineParams => lineParams[KeyName.LineSets].OfType<JsonData>())
                .Where(lineSetId => lineSetId.IsString)
                .Select(lineSetId => (string)lineSetId)
                .ToList();

            var relatedLineSetParams = lineNodes.Keys
                .Where(relatedLineSetIds.Contains)
                .Select(key => source[KeyName.LineNode][key])
                .Where(lineSet => lineSet.HasKeyAndValue(KeyName.NodeType)
                                  && lineSet[KeyName.NodeType].IsString
                                  && (string)lineSet[KeyName.NodeType] == "LineSet")
                .Where(lineSet => lineSet.HasKeyAndValue(KeyName.NodeParams)
                                  && lineSet[KeyName.NodeParams].IsObject)
                .Select(lineSet => lineSet[KeyName.NodeParams]);


            Func<JsonData, string, string, KeyValuePair<string, string>> fetchBrushSettings = 
                (lineSetParams, brushIdParamName, specificOnParamName) =>
                {
                    if (!shouldImportDisabledSpecificBrush
                        && lineSetParams.HasKeyAndValue(specificOnParamName)
                        && lineSetParams[specificOnParamName].IsBoolean)
                    {
                        var isSpecificOn = (bool)lineSetParams[specificOnParamName];
                        if (!isSpecificOn)
                        {
                            return new KeyValuePair<string, string>();
                        }
                    }

                    if (!lineSetParams.HasKeyAndValue(brushIdParamName)
                        || lineSetParams[brushIdParamName] == null
                        || !lineSetParams[brushIdParamName].IsString)
                    {
                        return new KeyValuePair<string, string>();
                    }

                    var brushId = (string)lineSetParams[brushIdParamName];
                    if (!lineNodes.HasKeyAndValue(brushId) 
                        || !lineNodes[brushId].IsObject 
                        || !lineNodes[brushId].HasKeyAndValue(KeyName.NodeParams) 
                        || !lineNodes[brushId][KeyName.NodeParams].IsObject)
                    {
                        return new KeyValuePair<string, string>();
                    }
                    
                    var brushParams = lineNodes[brushId][KeyName.NodeParams];
                    if (!brushParams.HasKeyAndValue("BrushDetail") || !brushParams["BrushDetail"].IsString)
                    {
                        return new KeyValuePair<string, string>();
                    }

                    var brushDetailId = (string)brushParams["BrushDetail"];
                    return new KeyValuePair<string, string>(brushId, brushDetailId);

                };
            

            Func<JsonData, string, string, string> fetchReductionSettingIds =
                (lineSetParams, reductionIdParamName, specificOnParamName) =>
                {
                    if (!shouldImportDisabledReduction
                        && lineSetParams.HasKeyAndValue(specificOnParamName)
                        && lineSetParams[specificOnParamName].IsBoolean)
                    {
                        var isSpecificOn = (bool)lineSetParams[specificOnParamName];
                        if (!isSpecificOn)
                        {
                            return null;
                        }
                    }
                    
                    if (!lineSetParams.HasKeyAndValue(reductionIdParamName) 
                        || !lineSetParams[reductionIdParamName].IsString)
                    {
                        return null;
                    }

                    return (string)lineSetParams[reductionIdParamName];
                };
                
           
            var brushSettingNames = new Dictionary<string, string>
            {
                {"VBrushSettings", null},
                {"HBrushSettings", null},
                {"VOutline", "VOutlineSpecificOn"},
                {"VObject", "VObjectSpecificOn"},
                {"VIntersection", "VIntersectionSpecificOn"},
                {"VSmooth", "VSmoothSpecificOn"},
                {"VMaterial", "VMaterialSpecificOn"},
                {"VSelected", "VSelectedSpecificOn"},
                {"VNormalAngle", "VNormalAngleSpecificOn"},
                {"VWireframe", "VWireframeSpecificOn"},
                {"HOutline", "HOutlineSpecificOn"},
                {"HObject", "HObjectSpecificOn"},
                {"HIntersection", "HIntersectionSpecificOn"},
                {"HSmooth", "HSmoothSpecificOn"},
                {"HMaterial", "HMaterialSpecificOn"},
                {"HSelected", "HSelectedSpecificOn"},
                {"HNormalAngle", "HNormalAngleSpecificOn"},
                {"HWireframe", "HWireframeSpecificOn"}
            };

            var reductionSettingNames = new Dictionary<string, string>
            {
                {"VSizeReduction", "VSizeReductionOn"},
                {"VAlphaReduction", "VAlphaReductionOn"},
                {"HSizeReduction", "HSizeReductionOn"},
                {"HAlphaReduction", "HAlphaReductionOn"}
            };

            var relatedSettingIds = new List<string>();
            foreach (var lineSetParams in relatedLineSetParams)
            {
                var localParams = lineSetParams;
                var brushSettingIdsToImport = brushSettingNames
                    .Select(x => fetchBrushSettings(localParams, x.Key, x.Value))
                    .Where(x => x.Key != null)
                    .SelectMany(x => new string[]{ x.Key, x.Value });
                relatedSettingIds.AddRange(brushSettingIdsToImport);

                var reductionSettingIdsToImport = reductionSettingNames
                    .Select(x => fetchReductionSettingIds(localParams, x.Key, x.Value))
                    .Where(x => x != null);
                relatedSettingIds.AddRange(reductionSettingIdsToImport);
            }

            return lineNodeIds.Concat(relatedLineSetIds).Concat(relatedSettingIds);
            
        }

        private static IEnumerable<string> CollectTexmapNodeIds(
            JsonData source,
            string nodeId)
        {
            if (!source.IsObject
                || !source.HasKeyAndValue(KeyName.LineNode)
                || !source[KeyName.LineNode].IsObject)
            {
                return new List<string>();
            }

            var lineNode = source[KeyName.LineNode][nodeId];
            if (lineNode == null)
            {
                return new List<string>();
            }

            var brushSettingNames = new Dictionary<string, string[]>
            {
                {Utils.GetJsonAlias(typeof(Pencil_4.BrushSettingsNode)), new string[] {
                    "ColorMap", "SizeMap",
                } },
                {Utils.GetJsonAlias(typeof(Pencil_4.BrushDetailNode)), new string[] {
                    "BrushMap", "DistortionMap",
                } },
            };

            var nodeParams = lineNode[KeyName.NodeParams];

            return brushSettingNames.Where(x => x.Key.Equals((string)lineNode[KeyName.NodeType]))
                .SelectMany(x => x.Value)
                .Where(x => nodeParams != null && nodeParams.HasKeyAndValue(x) && nodeParams[x].IsString)
                .Select(x => (string)nodeParams[x]);
        }

#endregion


#region Importer

        public static void RegisterImporters(Func<string, string, UnityEngine.Object> assetFinder)
        {
            _assetFinder = assetFinder;
            _customImporter = new Dictionary<Type, Action<CustomImporterArgs>>
            {
                {typeof(GameObject), ImportGameObject},
                {typeof(List<GameObject>), ImportGameObjectList},
                {typeof(List<Material>), ImportMaterialList},
                {typeof(AnimationCurve), ImportAnimationCurve},
                {typeof(float), ImportFloat},
                {typeof(Color), ImportColor},
                {typeof(Vector2), ImportVector2},
                {typeof(Texture2D), ImportTexture2D},
                {typeof(int), ImportInt},
                {typeof(bool), ImportBool},
                {typeof(string), ImportString},
                {typeof(BrushDetailNode.LoopDirection), ImportLoopDirection}
            };
        }

        private static void ImportOneParam(
            Component node,
            JsonData value,
            FieldInfo field,
            float scale,
            Func<string, GameObject> nodeFinder)
        {
            var args = new CustomImporterArgs
            {
                node = node,
                value = value,
                field = field,
                scale = scale,
                findNodeFunc = nodeFinder
            };
            
            if (_customImporter.ContainsKey(field.FieldType))
            {
                _customImporter[field.FieldType](args);
            }
            else if (field.FieldType.IsEnum)
            {
                _customImporter[typeof(int)](args);
            }
            else
            {
                throw new InvalidOperationException("Unsupported field type.");
            }
        }


        private static void ImportGameObject(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsString) return;
            
            if (args.field.HasAttribute<PencilNode>())
            {
                var childNode = args.findNodeFunc((string)args.value);
                if (childNode == null) return;
                
                args.SetValueByReflection(childNode);
                childNode.transform.parent = args.node.gameObject.transform;
            }
            else
            {
                var obj = GameObject.Find((string)args.value);
                if (obj == null) return;
                
                args.SetValueByReflection(obj);
            }
        }

        
        private static void ImportGameObjectList(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsArray) return;
            var newGameObjectList = new List<GameObject>();
            
            if (args.field.HasAttribute<PencilNode>())
            {
                foreach (var i in Enumerable.Range(0, args.value.Count))
                {
                    if (!args.value[i].IsString) continue;

                    var childNode = args.findNodeFunc((string)args.value[i]);
                    newGameObjectList.Add(childNode);
                    childNode.transform.parent = args.node.gameObject.transform;
                }
            }
            else
            {
                var objectNames = Enumerable.Range(0, args.value.Count)
                    .Select(i => args.value[i].IsString ? (string)args.value[i] : null)
                    .Where(x => x != null)
                    .Distinct();
                var allGameObjects = Object.FindObjectsOfType<GameObject>();
                foreach (var name in objectNames)
                {
                    newGameObjectList.AddRange(allGameObjects.Where(x => x.name == name));
                }
            }
            args.SetValueByReflection(newGameObjectList);
        }

        
        private static void ImportMaterialList(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsArray) return;

            var existingMaterials = Utils.EnumerateMaterials().ToList();
            var newMaterialList = Enumerable.Range(0, args.value.Count)
                .Select(i => args.value[i].TryGetStringValue("Name"))
                .SelectMany(x => existingMaterials.Where(y => y.name == EscapeMaterialName(x)))
                .ToList();

            args.SetValueByReflection(newMaterialList);
        }

        
        private static void ImportAnimationCurve(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsObject) return;
            var curveKinds = args.value.Keys;
#if UNITY_EDITOR
            if (curveKinds.Contains(KeyName.UnityCurveKeys))
            {
                ImportUnityCurve(args);
            }
            else 
#endif
            if (curveKinds.Contains(KeyName.UniversalCurveKeys))
            {
                ImportUniversalCurve(args);
            }
        }
        

        private static void ImportUnityCurve(CustomImporterArgs args)
        {
            if (args.value == null) return;

             var curveKeys = args.value[KeyName.UnityCurveKeys];
                if (!curveKeys.IsArray) return;

                var keyframes = new List<Keyframe>();
                
                foreach (var i in Enumerable.Range(0, curveKeys.Count))
                {
                    var time = Convert.ToSingle((double) curveKeys[i][KeyName.CurveTime]);
                    var value = Convert.ToSingle((double) curveKeys[i][KeyName.CurveValue]);
                    var inTangent = Convert.ToSingle((double) curveKeys[i][KeyName.CurveInTangent]);
                    var outTangent = Convert.ToSingle((double) curveKeys[i][KeyName.CurveOutTangent]);
#if UNITY_2018_1_OR_NEWER
                    var curveKeyDict = curveKeys[i] as IDictionary;
                    if (curveKeyDict.Contains(KeyName.CurveInWeight)
                        && curveKeyDict.Contains(KeyName.CurveOutWeight)
                        && curveKeyDict.Contains(KeyName.CurveWeightedMode))
                    {
                        var inWeight = Convert.ToSingle((double) curveKeys[i][KeyName.CurveInWeight]);
                        var outWeight = Convert.ToSingle((double) curveKeys[i][KeyName.CurveOutWeight]);
                        var newKeyframe = new Keyframe(time, value, inTangent, outTangent, inWeight, outWeight)
                        {
                            weightedMode = (WeightedMode) (int) curveKeys[i][KeyName.CurveWeightedMode]
                        };
                        keyframes.Add(newKeyframe);
                    }
                    else
                    {
                        keyframes.Add(new Keyframe(time, value, inTangent, outTangent));
                    }  
#else
                    keyframes.Add(new Keyframe(time, value, inTangent, outTangent));
#endif
                }
                
                var animationCurve = new AnimationCurve(keyframes.ToArray());

                for (var i = 0; i < curveKeys.Count; i++)
                {
#if UNITY_EDITOR
                    
                    AnimationUtility.SetKeyBroken(
                        animationCurve, i, (bool) curveKeys[i][KeyName.CurveKeyBroken]);
                    AnimationUtility.SetKeyLeftTangentMode(
                        animationCurve, i,
                        (AnimationUtility.TangentMode) (int) curveKeys[i][KeyName.CurveLeftTangentMode]);
                    AnimationUtility.SetKeyRightTangentMode(
                        animationCurve, i,
                        (AnimationUtility.TangentMode) (int) curveKeys[i][KeyName.CurveRightTangentMode]);
#endif
                }
                args.SetValueByReflection(animationCurve);
        }


        private static void ImportUniversalCurve(CustomImporterArgs args)
        {
            if (args.value == null) return;

            // UniversalKeysの読み込み
            var curveValues = args.value[KeyName.UniversalCurveKeys];
            if (!curveValues.IsArray)  return;

            var keyframes = curveValues.Cast<JsonData>()
                .Where(p => p.IsArray || p.Count < 2)
                .Select(p => new Keyframe(
                    Convert.ToSingle((double) p[0]),
                    Convert.ToSingle((double) p[1])));

            var animationCurve = new AnimationCurve(keyframes.ToArray());
            foreach (var i in Enumerable.Range(0, animationCurve.length))
            {
                animationCurve.SmoothTangents(i, 0);
            }
            args.SetValueByReflection(animationCurve);
        }

        
        private static void ImportFloat(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsDouble) return;
            
            if (args.field.HasAttribute<JsonScaleDependent>())
            {
                args.SetValueByReflection(Convert.ToSingle((double)args.value) * args.scale);
            }
            else
            {
                args.SetValueByReflection(Convert.ToSingle((double)args.value));
            }          
        }

        
        private static void ImportColor(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsArray || args.value.Count < 4) return;

            if (Enumerable.Range(0, 4).Any(i => !args.value[i].IsDouble)) return;
            
            var color = new Color(
                Convert.ToSingle((double) args.value[0]),
                Convert.ToSingle((double) args.value[1]),
                Convert.ToSingle((double) args.value[2]),
                Convert.ToSingle((double) args.value[3]));
            args.SetValueByReflection(color);
        }

        
        private static void ImportVector2(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsArray || args.value.Count < 2) return;
            
            if (Enumerable.Range(0, 2).Any(i => !args.value[i].IsDouble)) return;
            var vector = new Vector2(
                Convert.ToSingle((double)args.value[0]),
                Convert.ToSingle((double)args.value[1]));
            args.SetValueByReflection(vector);
        }


        private static void ImportTexture2D(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsString) return;
            
            var texture = _assetFinder("t:texture", (string)args.value) as Texture2D;
            args.SetValueByReflection(texture);
        }

        
        private static void ImportInt(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsInt && !args.value.IsLong) return;
            args.SetValueByReflection((int)args.value);
        }


        private static void ImportBool(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsBoolean) return;
            args.SetValueByReflection((bool)args.value);
        }


        private static void ImportString(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsString) return;
            args.SetValueByReflection((string)args.value);
        }

        
        private static void ImportLoopDirection(CustomImporterArgs args)
        {
            if (args.value == null || !args.value.IsInt) return;
            var value = (int)args.value;
            // 他プラットフォームでEnumの値が逆であるため、値を反転させる
            args.SetValueByReflection(value == 0 ? 1 : 0);
        }

#endregion
    }
}
