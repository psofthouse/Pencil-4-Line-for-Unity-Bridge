using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Pencil_4;
using Pencil4_Bridge;

namespace Pcl4Editor
{

    public class ImportListView : TreeView
    {
        private ImportOptionsModel _model;
        
        private class ImportListViewItem : TreeViewItem
        {
            public ImportOptionsModel.LineData LineData;
        }
        
        
        public ImportListView(TreeViewState state, ImportOptionsModel model) : base(state)
        {
            _model = model;
            showBorder = true;
            Reload();
        }

        
        protected override TreeViewItem BuildRoot()
        {
            return new TreeViewItem {id = 0, depth = -1, displayName = "root"};
        }

        
        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            return _model.PencilLines
                .Select((x, n) => new ImportListViewItem
                    {
                        id = n + 1, 
                        depth = 0,
                        displayName = x.NodeName,
                        LineData = x
                    })
                .Cast<TreeViewItem>()
                .ToList();
        }

        
        protected override void RowGUI(RowGUIArgs args)
        {
            var rowItem = (ImportListViewItem)args.item;
            rowItem.LineData.ShouldImport = EditorGUI.ToggleLeft(
                args.rowRect,
                args.item.displayName,
                rowItem.LineData.ShouldImport);
        }
    }
    
    
    public class ImportWindow : EditorWindow
    {
        private static ImportWindow _importWindow;

        private const int WindowWidth = 450;
        private const int WindowHeight = 500;

        private ImportOptionsModel _model;

        private ImportListView _lineListView;
       
        private Action<ImportOptionsModel> _importButtonHandler;
        

        private void InitListView()
        {
            _lineListView = new ImportListView(new TreeViewState(), _model);
        }
        
        
        private void OnGUI()
        {
            var outerSpaceStyle = new GUIStyle()
            {
                margin = new RectOffset(5, 5, 5, 5)
            };

            var boxStyle = new GUIStyle(GUI.skin.box)
            {
                margin = new RectOffset(5, 5, 5, 5)
            };
            
            var innerSpaceStyle = new GUIStyle()
            {
                margin = new RectOffset(5, 5, 5, 5)
            };

            using (new EditorGUILayout.VerticalScope(outerSpaceStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(boxStyle, GUILayout.MinHeight(60)))
                    {
                        PlaceOptionsGUI(innerSpaceStyle);
                    }

                    using (new EditorGUILayout.VerticalScope(boxStyle, GUILayout.MinHeight(60)))
                    {
                        PlaceUnitConversionGUI();
                    }
                }
                
                EditorGUILayout.LabelField("Pencil+ 4 Lines");
                
                var listViewRect = EditorGUILayout.GetControlRect(false, position.height - 170);
                _lineListView.OnGUI(listViewRect);

                _model.ShouldImportDisabledSpecificBrush = EditorGUILayout.ToggleLeft(
                    "Import disabled Specific Brush Settings",
                    _model.ShouldImportDisabledSpecificBrush);

                _model.ShouldImportDisabledReductionSettings = EditorGUILayout.ToggleLeft(
                    "Import disabled Reduction Settings",
                    _model.ShouldImportDisabledReductionSettings);

                EditorGUILayout.Space();
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (Application.platform == RuntimePlatform.OSXEditor)
                    {
                        PlaceCancelButton();
                        PlaceImportButton();
                    }
                    else
                    {
                        PlaceImportButton();
                        PlaceCancelButton();
                    }
                }   
            }
        }

        
        private void PlaceOptionsGUI(GUIStyle style)
        {
            EditorGUILayout.LabelField("Options");
            using (new EditorGUILayout.HorizontalScope(style))
            {
                EditorGUILayout.Space();
                var isReplace = EditorGUILayout.ToggleLeft(
                    "Replace",
                    _model.ImportOption == ImportOptionsModel.ImportOptions.Replace,
                    GUILayout.MaxWidth(90));
                            
                if (isReplace && _model.ImportOption != ImportOptionsModel.ImportOptions.Replace)
                {
                    _model.ImportOption = ImportOptionsModel.ImportOptions.Replace;
                }
                            
                var isMerge = EditorGUILayout.ToggleLeft(
                    "Merge",
                    _model.ImportOption == ImportOptionsModel.ImportOptions.Merge,
                    GUILayout.MaxWidth(90));
                            
                if (isMerge && _model.ImportOption != ImportOptionsModel.ImportOptions.Merge)
                {
                    _model.ImportOption = ImportOptionsModel.ImportOptions.Merge;
                }
                            
                EditorGUILayout.Space();
            }
        }


        private void PlaceUnitConversionGUI()
        {
            EditorGUILayout.LabelField("Unit Conversion");
            _model.IsUnitConversionAuto = EditorGUILayout.ToggleLeft("Auto", _model.IsUnitConversionAuto);
            using (new EditorGUI.DisabledScope(_model.IsUnitConversionAuto))
            {
                EditorGUI.indentLevel++;
                _model.ScaleFactor = EditorGUILayout.FloatField("Scale Factor", _model.ScaleFactor);
                EditorGUI.indentLevel--;
            }
        }
        

        private void PlaceImportButton()
        {
            if (GUILayout.Button("Import"))
            {
                _importButtonHandler(_model);
                Close();
            }
        }


        private void PlaceCancelButton()
        {                    
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
        }
        
        
        private static List<ImportOptionsModel.LineData> JsonDataToLineData(JsonData rootData)
        {
            if (!rootData.HasKeyAndValue(KeyName.LineNode) || !rootData[KeyName.LineNode].IsObject)
            {
                return new List<ImportOptionsModel.LineData>();
            }

            return rootData[KeyName.LineNode].Keys
                .Select(nodeId => new {nodeId, nodeData = rootData[KeyName.LineNode][nodeId]})
                .Where(x =>
                    x.nodeData.HasKeyAndValue(KeyName.NodeName) && x.nodeData[KeyName.NodeName].IsString &&
                    x.nodeData.HasKeyAndValue(KeyName.NodeType) && x.nodeData[KeyName.NodeType].IsString)
                .Where(x => (string)x.nodeData[KeyName.NodeType] == "Line")
                .Select(x => new ImportOptionsModel.LineData
                {
                    NodeId = x.nodeId, NodeName = (string)x.nodeData[KeyName.NodeName], ShouldImport = true
                })
                .ToList();
        }


        public static void OpenWithJsonData(JsonData jsonData, Action<ImportOptionsModel> importButtonHandler)
        {          
            if (_importWindow == null)
            {
                _importWindow = CreateInstance<ImportWindow>();
            }

            var packageInfo = Pcl4PackageInfo.LoadFromGUID(BridgeEditor.PackageManifestGUID);
            if (packageInfo != null)
            {
                _importWindow.titleContent = new GUIContent(packageInfo.displayName + " " + packageInfo.version);
            }

            _importWindow._model = new ImportOptionsModel
            {
                PencilLines = JsonDataToLineData(jsonData)
            };
            _importWindow._importButtonHandler = importButtonHandler;
            _importWindow.InitListView();
            _importWindow.ShowUtility();

            _importWindow.position = new Rect(
                (Screen.currentResolution.width / 2) - (WindowWidth / 2),
                (Screen.currentResolution.height / 2) - (WindowHeight / 2),
                WindowWidth,
                WindowHeight);
        }
        
        public static void ImportNode(string jsonPath, LineListNode lineListNode)
        {
            string jsonString;
            using (var streamReader = new StreamReader(jsonPath))
            {
                jsonString = streamReader.ReadToEnd();
            }

            var jsonData = JsonMapper.ToObject(jsonString);

            Utils.ValidateJsonDataStructure(jsonData);

            OpenWithJsonData(jsonData, options =>
            {
                Importer.ImportNodeWithOptions(jsonData, options, lineListNode);
            });
        }

    }

}

