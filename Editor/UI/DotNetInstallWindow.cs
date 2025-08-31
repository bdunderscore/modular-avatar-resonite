using System;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace nadena.dev.ndmf.platform.resonite
{
    internal sealed class DotNetInstallWindow : EditorWindow
    {
        const string ResourcesRoot = "Packages/nadena.dev.modular-avatar.resonite/Editor/UI/Resources/";

        private void OnEnable()
        {
            minSize = new Vector2(100, 100);
        }

        private void CreateGUI()
        {
            // Import UXML
            var visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    ResourcesRoot + "DotNetCheck.uxml");
            VisualElement rootFromUXML = visualTree.CloneTree();
            
            rootVisualElement.Add(rootFromUXML);

            var styleSheet =
                AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    ResourcesRoot + "ResoniteBuildUI.uss");
            rootFromUXML.styleSheets.Add(styleSheet);

            rootFromUXML.Q<Button>("btn-install").clicked += () =>
            {
                // Open URL
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Application.OpenURL(
                        "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-9.0.4-windows-x64-installer");
                else
                    Application.OpenURL("https://dotnet.microsoft.com/en-us/download/dotnet/9.0");
            };
        }
    }
}