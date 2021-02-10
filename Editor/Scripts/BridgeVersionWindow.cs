using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Pcl4Editor
{
    public class BridgeVersionWindow : VersionWindow
    {
        [MenuItem("Pencil+ 4/About Bridge", false, 3)]
        private static void Open()
        {
            OpenWithPackageManifestGUID(BridgeEditor.PackageManifestGUID);
        }
    }
}