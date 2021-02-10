using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Pencil_4;

namespace Pencil4_Bridge
{
    
    public static class FileVersion
    {
        /// <summary>
        /// ファイルバージョン
        /// </summary>
        private static readonly int[] Value = {1, 1};

        /// <summary>
        /// 読み込み可能なファイルバージョンの下限
        /// </summary>
        private static readonly int[] SupportedFileVersionMin = {1, 0};

        /// <summary>
        /// 読み込み不可能なファイルバージョンの下限
        /// </summary>
        private static readonly int[] UnsupportedFileVersionMin = {2, 0};

        public static string ToVersionString()
        {
            return Value[0].ToString() + "." + Value[1].ToString();
        }
        
        public static bool IsSupported(string version)
        {
            if (version == null)
            {
                return false;
            }

            var versions = version.Split('.');
            if (versions.Length != 2)
            {
                return false;
            }

            int majorVersion;
            if (!int.TryParse(versions[0], out majorVersion))
            {
                return false;
            }

            int minorVersion;
            if (!int.TryParse(versions[1], out minorVersion))
            {
                return false;
            }

            var majorMin = SupportedFileVersionMin[0];
            var minorMin = SupportedFileVersionMin[1];
            var majorMax = UnsupportedFileVersionMin[0];
            var minorMax = UnsupportedFileVersionMin[1];

            return majorVersion == majorMin && minorMin <= minorVersion
                   || majorMin < majorVersion && majorVersion < majorMax
                   || majorVersion == majorMax && minorVersion < minorMax;
        }    
        
    }
    
    
    public static class KeyName
    {
        public const string Platform = "Platform";
        public const string JsonFileVersion = "FileVersion";
        public const string ScaleFactor = "ScaleFactor";
        public const string LineNode = "LineNode";
        public const string MaterialNode = "MaterialNode";

        public const string NodeType = "NodeType";
        public const string NodeName = "NodeName";
        public const string NodeParams = "Params";

        public const string LineSets = "LineSets";

        public const string UnityCurveKeys = "UnityCurveKeys";
        public const string UniversalCurveKeys = "UniversalKeys";
        public const string CurveTime = "Time";
        public const string CurveValue = "Value";
        public const string CurveInTangent = "InTangent";
        public const string CurveOutTangent = "OutTangent";
        public const string CurveLeftTangentMode = "LeftTangentMode";
        public const string CurveRightTangentMode = "RightTangentMode";
        public const string CurveKeyBroken = "KeyBroken";
        public const string CurveInWeight = "InWeight";
        public const string CurveOutWeight = "OutWeight";
        public const string CurveWeightedMode = "WeightedMode";
    }

    public static class Utils
    {
        public static string GetJsonAlias(Type type)
        {
            var attributes = type.GetCustomAttributes(typeof(JsonAlias), false);
            return attributes.Length > 0 ? ((JsonAlias)attributes[0]).Alias : type.ToString();
        }
        
        public static IEnumerable<Material> EnumerateMaterials()
        {
            return Resources.FindObjectsOfTypeAll<Material>()
                .Where(x => x.hideFlags == HideFlags.None || x.hideFlags == HideFlags.NotEditable)
                .Where(x => !x.name.StartsWith("Hidden/"));
            // Hidden/以下のマテリアルが混入するので、差し当たりの対応
        }
        
        public static void ValidateJsonDataStructure(JsonData jsonData)
        {
            if (!jsonData.IsObject)
            {
                throw new InvalidOperationException("Invalid format.");
            }

            var rootDict = jsonData as IDictionary;

            if (!rootDict.Contains(KeyName.JsonFileVersion))
            {
                throw new InvalidOperationException("File Version not found.");
            }

            if (!FileVersion.IsSupported(jsonData[KeyName.JsonFileVersion].ToString()))
            {
                throw new InvalidOperationException("Invalid File Version.");
            }

            if (!rootDict.Contains(KeyName.LineNode)
                || !jsonData[KeyName.LineNode].IsObject)
            {
                throw new InvalidOperationException("Line Node List not found.");
            }

            if (!rootDict.Contains(KeyName.MaterialNode)
                || !jsonData[KeyName.MaterialNode].IsObject)
            {
                throw new InvalidOperationException("Material Node List not found.");
            }

        }
    }
    
    
}
