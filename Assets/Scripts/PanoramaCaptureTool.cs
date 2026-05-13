using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PanoramaCaptureTool : MonoBehaviour
{
    public enum AngleSamplingMode
    {
        AutoGenerate,
        CustomArray
    }

    [Header("Angle Sampling Mode")]
    public AngleSamplingMode angleSamplingMode = AngleSamplingMode.AutoGenerate;

    [Header("Custom Angle Arrays (Used when mode = CustomArray)")]
    [Tooltip("Pitch angles in degrees, e.g. -30, 0, 30")]
    public float[] customPitchAngles;

    [Tooltip("Yaw angles in degrees, e.g. 0, 45, 90, 180")]
    public float[] customYawAngles;

    [Header("Camera")]
    public Camera captureCamera;

    [Header("360 Image Batch Input")]
    [Tooltip("要被替換貼圖的材質（例如 Skybox 材質）")]
    public Material panoramaMaterial;

    [Tooltip("存放 360 圖片的資料夾「絕對路徑」")]
    public string panoramaImageFolderPath;

    [Header("Output")]
    public int resolution = 1024;
    public float fieldOfView = 60f;
    public string outputFolderName = "PanoramaOutput";

    [Header("Angular Sampling")]
    [Tooltip("X 軸（Pitch）分組數，不包含 ±90°")]
    public int pitchGroupCount = 5;

    [Tooltip("Y 軸（Yaw）分組數，360° 環狀")]
    public int yawGroupCount = 12;

    [Header("Capture Timing")]
    [Tooltip("每一張照片之間等待的時間（秒）")]
    public float delayBetweenShots = 0.1f;

    void Start()
    {
        ValidateParameters();

        if (panoramaMaterial == null)
            throw new Exception("尚未指定 panoramaMaterial");

        if (string.IsNullOrEmpty(panoramaImageFolderPath) || !Directory.Exists(panoramaImageFolderPath))
            throw new Exception("panoramaImageFolderPath 無效或不存在");

        List<float> pitchAngles;
        List<float> yawAngles;

        GetSamplingAngles(out pitchAngles, out yawAngles);

        SetupCamera();
        //StartCoroutine(CaptureAll(pitchAngles, yawAngles));
        StartCoroutine(CaptureAllPanoramas(pitchAngles, yawAngles));
    }
    void GetSamplingAngles(out List<float> pitchAngles, out List<float> yawAngles)
    {
        pitchAngles = new List<float>();
        yawAngles = new List<float>();

        if (angleSamplingMode == AngleSamplingMode.CustomArray)
        {
            if (customPitchAngles == null || customPitchAngles.Length == 0)
                throw new Exception("Custom mode 啟用，但 customPitchAngles 為空");

            if (customYawAngles == null || customYawAngles.Length == 0)
                throw new Exception("Custom mode 啟用，但 customYawAngles 為空");

            pitchAngles.AddRange(customPitchAngles);
            yawAngles.AddRange(customYawAngles);
        }
        else
        {
            // 原本行為
            ValidateParameters();
            pitchAngles = GeneratePitchAngles(pitchGroupCount);
            yawAngles = GenerateYawAngles(yawGroupCount);
        }
    }

    // =========================
    // 驗證參數
    // =========================
    void ValidateParameters()
    {
        int pitchDivisor = pitchGroupCount + 1;
        if (180 % pitchDivisor != 0)
        {
            throw new Exception(
                $"Pitch 設定錯誤：180 無法被 (pitchGroupCount + 1) = {pitchDivisor} 整除"
            );
        }

        if (360 % yawGroupCount != 0)
        {
            throw new Exception(
                $"Yaw 設定錯誤：360 無法被 yawGroupCount = {yawGroupCount} 整除"
            );
        }

        if (captureCamera == null)
        {
            throw new Exception("尚未指定 captureCamera");
        }
    }

    // =========================
    // Camera 初始化
    // =========================
    void SetupCamera()
    {
        captureCamera.transform.position = Vector3.zero;
        captureCamera.fieldOfView = fieldOfView;
        captureCamera.clearFlags = CameraClearFlags.Skybox;
    }

    // =========================
    // 產生 Pitch 角度
    // =========================
    List<float> GeneratePitchAngles(int count)
    {
        List<float> result = new List<float>();
        float step = 180f / (count + 1);

        for (int i = 1; i <= count; i++)
        {
            float pitch = 90f - i * step;
            result.Add(pitch);
        }

        return result;
    }

    // =========================
    // 產生 Yaw 角度
    // =========================
    List<float> GenerateYawAngles(int count)
    {
        List<float> result = new List<float>();
        float step = 360f / count;

        for (int i = 0; i < count; i++)
        {
            result.Add(i * step);
        }

        return result;
    }

    // =========================
    // 主拍攝流程
    // =========================
    // =========================
    // 主拍攝流程（Coroutine 版本）
    // =========================
    IEnumerator CaptureAll(List<float> pitchAngles, List<float> yawAngles)
    {
        // 根輸出資料夾
        string rootDir = Path.Combine(Application.dataPath, outputFolderName);
        Directory.CreateDirectory(rootDir);

        // 依照日期時間建立子資料夾
        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string sessionDir = Path.Combine(rootDir, timeStamp);
        Directory.CreateDirectory(sessionDir);

        Debug.Log($"Panorama capture output folder: {sessionDir}");

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        rt.antiAliasing = 1;
        rt.filterMode = FilterMode.Point;

        Texture2D tex = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGB24,
            false
        );
        tex.filterMode = FilterMode.Point;

        captureCamera.targetTexture = rt;

        foreach (float pitch in pitchAngles)
        {
            foreach (float yaw in yawAngles)
            {
                // 設定相機角度
                captureCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

                // 等一幀，確保角度與 Skybox 生效
                yield return null;

                // 主動渲染
                captureCamera.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                tex.Apply();

                string filename = $"cam_x{pitch:+0;-0}_y{yaw:000}.png";
                string path = Path.Combine(sessionDir, filename);
                File.WriteAllBytes(path, tex.EncodeToPNG());

                // 再等一幀，避免 GPU / CPU 壓力過大
                yield return new WaitForSeconds(delayBetweenShots);
            }
        }

        captureCamera.targetTexture = null;
        RenderTexture.active = null;

        Destroy(rt);
        Destroy(tex);

        // 拍攝完成後，相機角度歸零
        captureCamera.transform.localRotation = Quaternion.identity;

        Debug.Log("Panorama capture completed.");
    }
    IEnumerator CaptureAllPanoramas(List<float> pitchAngles, List<float> yawAngles)
    {
        // === 建立一次性的輸出資料夾 ===
        string rootDir = Path.Combine(Application.dataPath, outputFolderName);
        Directory.CreateDirectory(rootDir);

        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string sessionDir = Path.Combine(rootDir, timeStamp);
        Directory.CreateDirectory(sessionDir);

        Debug.Log($"Panorama batch output folder: {sessionDir}");

        // === 準備 RenderTexture ===
        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        rt.antiAliasing = 1;
        rt.filterMode = FilterMode.Point;

        Texture2D tex = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGB24,
            false
        );
        tex.filterMode = FilterMode.Point;

        captureCamera.targetTexture = rt;

        // === 讀取所有 360 圖 ===
        string[] imageFiles = Directory.GetFiles(panoramaImageFolderPath);

        foreach (string imagePath in imageFiles)
        {
            if (!imagePath.EndsWith(".jpg") &&
                !imagePath.EndsWith(".png") &&
                !imagePath.EndsWith(".jpeg"))
                continue;

            Debug.Log($"Processing panorama: {imagePath}");

            // === 載入 360 圖 ===
            byte[] imageData = File.ReadAllBytes(imagePath);
            Texture2D panoramaTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            panoramaTex.LoadImage(imageData);
            panoramaTex.Apply();

            // === 套用到指定材質 ===
            panoramaMaterial.mainTexture = panoramaTex;

            // 等一幀，確保材質與 Skybox 生效
            yield return null;

            string panoName = Path.GetFileNameWithoutExtension(imagePath);

            // === 對「這一張 360 圖」做完整角度截圖 ===
            foreach (float pitch in pitchAngles)
            {
                foreach (float yaw in yawAngles)
                {
                    captureCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

                    yield return null;

                    captureCamera.Render();

                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    tex.Apply();

                    string filename =
                        $"{panoName}_x{pitch:+0;-0}_y{yaw:000}.png";

                    string path = Path.Combine(sessionDir, filename);
                    File.WriteAllBytes(path, tex.EncodeToPNG());

                    yield return new WaitForSeconds(delayBetweenShots);
                }
            }

            Destroy(panoramaTex);
        }

        // === 清理 ===
        captureCamera.targetTexture = null;
        RenderTexture.active = null;

        Destroy(rt);
        Destroy(tex);

        captureCamera.transform.localRotation = Quaternion.identity;

        Debug.Log("All panorama batch capture completed.");
    }
}
