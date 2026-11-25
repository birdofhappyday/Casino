
#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

public static class iOSSignInPostProcess
{
    private const string EntitlementsFileName =
        "Unity-iPhone.entitlements";

    [PostProcessBuild(1000)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS)
            return;

        try
        {
            Debug.Log("[iOSPostProcess] Start");

            // 1. Info.plist 권한 설정
            ConfigureInfoPlist(buildPath);

            // 2. Xcode 프로젝트 로드
            string projectPath = PBXProject.GetPBXProjectPath(buildPath);

            PBXProject project = new PBXProject();
            project.ReadFromFile(projectPath);

            string mainTargetGuid =
                project.GetUnityMainTargetGuid();

            // 3. Entitlements 설정
            string entitlementsPath = project.GetBuildPropertyForAnyConfig(mainTargetGuid, "CODE_SIGN_ENTITLEMENTS");

            if (string.IsNullOrEmpty(entitlementsPath))
            {
                entitlementsPath = EntitlementsFileName;
            }

            // 4. Capability 추가
            ProjectCapabilityManager capabilityManager = new ProjectCapabilityManager(projectPath, entitlementsPath, targetGuid: mainTargetGuid);

            // Sign in with Apple
            capabilityManager.AddSignInWithApple();

            // Push Notifications
            capabilityManager.AddPushNotifications();

            // 5. Xcode 프로젝트 저장
            capabilityManager.WriteToFile();

            Debug.Log("[iOSPostProcess] Complete");
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[iOSPostProcess] Failed: " + e);

            throw;
        }
    }

    private static void ConfigureInfoPlist(string buildPath)
    {
        string plistPath = Path.Combine(buildPath, "Info.plist");

        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        PlistElementDict root = plist.root;

        // Camera
        SetUsageDescription(root, "NSCameraUsageDescription", "카메라 권한 요청");

        // Photo Library
        SetUsageDescription(root, "NSPhotoLibraryUsageDescription", "사진 라이브러리 접근 권한 요청");

        // Photo Library Add
        SetUsageDescription(root, "NSPhotoLibraryAddUsageDescription", "사진 저장 권한 요청");

        // Microphone
        SetUsageDescription(root, "NSMicrophoneUsageDescription", "마이크 권한 요청");

        // Location
        SetUsageDescription(root, "NSLocationWhenInUseUsageDescription", "위치 권한 요청");

        plist.WriteToFile(plistPath);

        Debug.Log("[iOSPostProcess] Info.plist configured");
    }

    private static void SetUsageDescription(PlistElementDict root, string key, string description)
    {
        root.SetString(key, description);
    }
}

#endif
