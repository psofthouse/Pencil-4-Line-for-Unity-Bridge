using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Pencil_4;
using Pencil4_Bridge;

namespace Pcl4Editor
{
    sealed class BridgeEditor
    {
        public static string PackageManifestGUID = "2672ff3bf69654d42a9eb759e9474b12";

        public static void Import(LineListNode target)
        {
            if (!target)
            {
                return;
            }

            Importer.RegisterImporters(Pcl4EditorUtilities.FindAssetInProjectOnEditor);

            var openPath = EditorUtility.OpenFilePanel(
                "Pencil+ 4 Bridge Import",
                StaticParameter.instance.BridgeLastOpenedPath ?? Application.dataPath,
                "json");
            if (string.IsNullOrEmpty(openPath))
            {
                return;
            }

            StaticParameter.instance.BridgeLastOpenedPath = openPath;

            var lineListNode = target as LineListNode;
            if (lineListNode != null)
            {
                ImportWindow.ImportNode(openPath, lineListNode);
            }
        }

        public static void Export(LineListNode target)
        {
            if (!target)
            {
                return;
            }

            var savePath = EditorUtility.SaveFilePanel(
                "Pencil+ 4 Bridge Export",
                StaticParameter.instance.BridgeLastOpenedPath ?? Application.dataPath,
                "",
                "json");
            if (string.IsNullOrEmpty(savePath))
            {
                return;
            }

            StaticParameter.instance.BridgeLastOpenedPath = savePath;

            var lineListNode = target as LineListNode;
            if (lineListNode != null)
            {
                Exporter.ExportNode(savePath, lineListNode);
            }
        }
    }
    }