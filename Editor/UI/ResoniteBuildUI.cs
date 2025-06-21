using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace nadena.dev.ndmf.platform.resonite
{
    internal class ResoniteBuildUI : BuildUIElement
    {
        const string ResourcesRoot = "Packages/nadena.dev.modular-avatar.resonite/Editor/UI/Resources/"; 
        private UnityEngine.GameObject _avatarRoot;
        private Button _buildButton, _copyPathButton, _saveAsButton;

        private Label _buildStateLabel;
        private VisualElement _postBuildButtonContainer, _buildStateContainer;
        
        public override UnityEngine.GameObject AvatarRoot
        {
            get => _avatarRoot;
            set
            {
                _avatarRoot = value;
                _buildButton?.SetEnabled(_avatarRoot != null);
            }
        }

        public ResoniteBuildUI()
        {
            // Import UXML
            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    ResourcesRoot + "ResoniteBuildUI.uxml");
            VisualElement rootFromUXML = visualTree.CloneTree();
            Add(rootFromUXML);

            var styleSheet =
                AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    ResourcesRoot + "ResoniteBuildUI.uss");
            styleSheets.Add(styleSheet);

            _buildStateContainer = rootFromUXML.Q<VisualElement>("build-state-container");
            _postBuildButtonContainer = rootFromUXML.Q<VisualElement>("build-hbox");
            _buildStateLabel = rootFromUXML.Q<Label>("build-state-label");
            _copyPathButton = rootFromUXML.Q<Button>("copy-path");
            _saveAsButton = rootFromUXML.Q<Button>("save-as");
            
            _copyPathButton.clicked += () =>
            {
                if (BuildController.Instance.LastTempPath != null)
                {
                    EditorGUIUtility.systemCopyBuffer = BuildController.Instance.LastTempPath;
                }
            };
            
            _saveAsButton.clicked += () =>
            {
                var path = EditorUtility.SaveFilePanel("Save resonite package", "", BuildController.Instance.LastAvatarName + ".resonitepackage", "resonitepackage");
                if (string.IsNullOrEmpty(path)) return;
                
                // Copy temp path to this path
                System.IO.File.Copy(BuildController.Instance.LastTempPath, path, true);
            };
            
            _buildButton = rootFromUXML.Q<Button>("build");
            _buildButton.clickable.clicked += () =>
            {
                BuildAvatar();
            };

            this.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                _buildStateContainer.style.display = DisplayStyle.None;
                BuildController.Instance.OnStateUpdate += UpdateDisplayState;
                UpdateDisplayState();
            });
            
            this.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                BuildController.Instance.OnStateUpdate -= UpdateDisplayState;
            });

            UpdateDisplayState();
        }

        private void UpdateDisplayState()
        {
            _postBuildButtonContainer.SetEnabled(!BuildController.Instance.IsBuilding);
            _buildButton.SetEnabled(!BuildController.Instance.IsBuilding && _avatarRoot != null);
            _buildButton.text = BuildController.Instance.IsBuilding ? "Build in progress..." : "Build";
            _buildStateLabel.text = BuildController.Instance.State;
        }

        private async void BuildAvatar()
        {
            // Start the server in the background
            using var client = RPCClientController.ClientHandle();
            
            var clone = GameObject.Instantiate(_avatarRoot);
            clone.name = clone.name.Substring(0, clone.name.Length - "(clone)".Length);
            try
            {
                using var scope = new AmbientPlatform.Scope(ResonitePlatform.Instance);
                using var scope2 = new OverrideTemporaryDirectoryScope(null);

                var buildContext = AvatarProcessor.ProcessAvatar(clone, ResonitePlatform.Instance);

                var root = await new AvatarSerializer().Export(clone, buildContext.GetState<ResoniteBuildState>().cai);
                await BuildController.Instance.BuildAvatar(client, root);
                
                _buildStateContainer.style.display = DisplayStyle.Flex;
                UpdateDisplayState();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }
    }
}