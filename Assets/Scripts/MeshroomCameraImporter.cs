using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// Meshroom cameras.sfm Importer
/// 已更新：
/// 1. 使用目前驗證過較正確的 pose 解讀方式
/// 2. 自動計算 FOV（高精度，不省略小數）
/// 3. 所有產生的 Camera 自動套用 FOV
///
/// Position = -center
/// Forward  = -column3
/// Up       = column2
///
/// FOV公式:
/// verticalFov = 2 * atan(sensorHeight / (2 * focalLength))
public class MeshroomCameraImporter : MonoBehaviour
{
    [Header("Input")]
    public string sfmFilePath;

    [Header("Output")]
    public GameObject cameraPrefab;
    public Transform parentRoot;

    [Header("Options")]
    public bool createUnityCamera = true;

    [Header("Gizmo")]
    public float gizmoSphereSize = 0.02f;
    public float gizmoRayLength = 0.25f;

    private List<GameObject> spawned = new List<GameObject>();

    private Dictionary<string, IntrinsicData> intrinsicMap =
        new Dictionary<string, IntrinsicData>();

    void Start()
    {
        Import();
    }

    [ContextMenu("Import Cameras")]
    public void Import()
    {
        ClearOld();

        if (!File.Exists(sfmFilePath))
        {
            Debug.LogError("File not found: " + sfmFilePath);
            return;
        }

        string json = File.ReadAllText(sfmFilePath);
        SfmRoot data = JsonUtility.FromJson<SfmRoot>(json);

        if (data == null)
        {
            Debug.LogError("Parse failed.");
            return;
        }

        intrinsicMap.Clear();

        if (data.intrinsics != null)
        {
            foreach (var intr in data.intrinsics)
            {
                intrinsicMap[intr.intrinsicId] = intr;
            }
        }

        Dictionary<string, PoseData> poseMap =
            new Dictionary<string, PoseData>();

        if (data.poses != null)
        {
            foreach (var p in data.poses)
            {
                poseMap[p.poseId] = p.pose;
            }
        }

        if (data.views == null)
        {
            Debug.LogError("No views found.");
            return;
        }

        foreach (var view in data.views)
        {
            if (!poseMap.ContainsKey(view.poseId))
                continue;

            PoseData pose = poseMap[view.poseId];

            GameObject go;

            if (cameraPrefab != null)
                go = Instantiate(cameraPrefab);
            else
                go = new GameObject();

            go.name = Path.GetFileNameWithoutExtension(view.path);

            if (parentRoot != null)
                go.transform.SetParent(parentRoot);

            ApplyPose(go.transform, pose);

            Camera cam = go.GetComponent<Camera>();

            if (createUnityCamera && cam == null)
                cam = go.AddComponent<Camera>();

            if (cam != null)
            {
                ApplyIntrinsics(cam, view.intrinsicId);
            }

            spawned.Add(go);
        }

        Debug.Log("Imported Cameras: " + spawned.Count);
    }

    void ApplyPose(Transform t, PoseData pose)
    {
        float[] r = ParseFloatArray(pose.transform.rotation);
        float[] c = ParseFloatArray(pose.transform.center);

        Matrix4x4 R = Matrix4x4.identity;

        R.m00 = r[0]; R.m01 = r[1]; R.m02 = r[2];
        R.m10 = r[3]; R.m11 = r[4]; R.m12 = r[5];
        R.m20 = r[6]; R.m21 = r[7]; R.m22 = r[8];

        Vector3 pos = new Vector3(
            -c[0],
            -c[1],
            -c[2]
        );

        Vector3 forward = -new Vector3(
            R.m02,
            R.m12,
            R.m22
        );

        Vector3 up = new Vector3(
            R.m01,
            R.m11,
            R.m21
        );

        t.position = pos;
        t.rotation = Quaternion.LookRotation(forward, up);
    }

    void ApplyIntrinsics(Camera cam, string intrinsicId)
    {
        if (!intrinsicMap.ContainsKey(intrinsicId))
            return;

        IntrinsicData intr = intrinsicMap[intrinsicId];

        double sensorHeight =
            ParseDouble(intr.sensorHeight);

        double focalLength =
            ParseDouble(intr.focalLength);

        // 高精度 FOV 計算（垂直視角）
        double fovRad =
            2.0d *
            Math.Atan(
                sensorHeight /
                (2.0d * focalLength)
            );

        double fovDeg =
            fovRad * (180.0d / Math.PI);

        cam.fieldOfView = (float)fovDeg;
        if (intr.principalPoint != null &&
    intr.principalPoint.Length >= 2)
        {
            double ppx =
                ParseDouble(intr.principalPoint[0]);

            double ppy =
                ParseDouble(intr.principalPoint[1]);

            double imgW =
                ParseDouble(intr.width);

            double imgH =
                ParseDouble(intr.height);

            double shiftX =
                ppx / (imgW * 0.5d);

            double shiftY =
                ppy / (imgH * 0.5d);

            // 套用 principalPoint（不影響 FOV）
            Matrix4x4 proj = cam.projectionMatrix;

            proj.m02 = (float)shiftX;
            proj.m12 = (float)shiftY;

            cam.projectionMatrix = proj;

            Debug.Log(
                $"[{cam.name}] PrincipalPoint = " +
                $"({ppx:F6}, {ppy:F6})  " +
                $"Shift = ({shiftX:F6}, {shiftY:F6})"
            );
        }
        Debug.Log(
            $"[{cam.name}] FOV = {fovDeg:F15}"
        );
    }

    float[] ParseFloatArray(string[] arr)
    {
        float[] result = new float[arr.Length];

        for (int i = 0; i < arr.Length; i++)
        {
            result[i] = float.Parse(
                arr[i],
                System.Globalization.CultureInfo.InvariantCulture
            );
        }

        return result;
    }

    double ParseDouble(string s)
    {
        return double.Parse(
            s,
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    void ClearOld()
    {
        foreach (var go in spawned)
        {
            if (go != null)
                DestroyImmediate(go);
        }

        spawned.Clear();
    }

    void OnDrawGizmos()
    {
        if (spawned == null)
            return;

        foreach (var go in spawned)
        {
            if (go == null)
                continue;

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(
                go.transform.position,
                gizmoSphereSize
            );

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(
                go.transform.position,
                go.transform.forward * gizmoRayLength
            );
        }
    }

    [Serializable]
    public class SfmRoot
    {
        public ViewData[] views;
        public IntrinsicData[] intrinsics;
        public PoseWrapper[] poses;
    }

    [Serializable]
    public class ViewData
    {
        public string path;
        public string poseId;
        public string intrinsicId;
    }

    [Serializable]
    public class IntrinsicData
    {
        public string intrinsicId;
        public string sensorWidth;
        public string sensorHeight;
        public string focalLength;

        public string width;
        public string height;
        public string[] principalPoint;
    }

    [Serializable]
    public class PoseWrapper
    {
        public string poseId;
        public PoseData pose;
    }

    [Serializable]
    public class PoseData
    {
        public TransformData transform;
    }

    [Serializable]
    public class TransformData
    {
        public string[] rotation;
        public string[] center;
    }
}