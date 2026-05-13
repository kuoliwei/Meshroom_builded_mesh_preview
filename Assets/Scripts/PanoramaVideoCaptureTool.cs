using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class PanoramaVideoCaptureTool : MonoBehaviour
{
    [Header("Camera")]
    public Camera captureCamera;

    [Header("Video Source")]
    public VideoPlayer videoPlayer;

    [Tooltip("每播放幾秒截圖一次")]
    public float captureIntervalSeconds = 2f;

    [Header("Output")]
    public int resolution = 1024;
    public float fieldOfView = 60f;
    public string outputFolderName = "PanoramaVideoOutput";

    [Header("Angle Sampling Mode")]
    public AngleSamplingMode angleSamplingMode = AngleSamplingMode.AutoGenerate;

    [Header("Auto Generate Settings")]
    public int pitchGroupCount = 5;
    public int yawGroupCount = 12;

    [Header("Custom Angle Arrays")]
    public float[] customPitchAngles;
    public float[] customYawAngles;

    [Header("Capture Timing")]
    public float delayBetweenShots = 0.05f;

    private bool videoFinished = false;
    public enum AngleSamplingMode
    {
        AutoGenerate,
        CustomArray
    }
    void OnEnable()
    {
        if (videoPlayer != null)
            videoPlayer.loopPointReached += OnVideoFinished;
    }

    void OnDisable()
    {
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= OnVideoFinished;
    }

    void OnVideoFinished(VideoPlayer vp)
    {
        Debug.Log("Video reached end.");
        videoFinished = true;
    }
    void Start()
    {
        if (captureCamera == null)
            throw new Exception("尚未指定 captureCamera");

        if (videoPlayer == null)
            throw new Exception("尚未指定 videoPlayer");

        SetupCamera();

        List<float> pitchAngles;
        List<float> yawAngles;

        GetSamplingAngles(out pitchAngles, out yawAngles);

        StartCoroutine(CaptureFromVideo(pitchAngles, yawAngles));
    }

    void SetupCamera()
    {
        captureCamera.transform.position = Vector3.zero;
        captureCamera.fieldOfView = fieldOfView;
        captureCamera.clearFlags = CameraClearFlags.Skybox;
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
            ValidateParameters();
            pitchAngles = GeneratePitchAngles(pitchGroupCount);
            yawAngles = GenerateYawAngles(yawGroupCount);
        }
    }

    void ValidateParameters()
    {
        int pitchDivisor = pitchGroupCount + 1;

        if (180 % pitchDivisor != 0)
            throw new Exception($"Pitch 設定錯誤：180 無法被 (pitchGroupCount + 1) = {pitchDivisor} 整除");

        if (360 % yawGroupCount != 0)
            throw new Exception($"Yaw 設定錯誤：360 無法被 yawGroupCount = {yawGroupCount} 整除");
    }

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

    IEnumerator CaptureFromVideo(List<float> pitchAngles, List<float> yawAngles)
    {
        if (!videoPlayer.isPrepared)
        {
            videoPlayer.Prepare();
            while (!videoPlayer.isPrepared)
                yield return null;
        }

        string rootDir = Path.Combine(Application.dataPath, outputFolderName);
        Directory.CreateDirectory(rootDir);

        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string sessionDir = Path.Combine(rootDir, timeStamp);
        Directory.CreateDirectory(sessionDir);

        Debug.Log($"Video capture output folder: {sessionDir}");

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);

        captureCamera.targetTexture = rt;

        videoFinished = false;
        videoPlayer.Play();

        double nextCaptureTime = captureIntervalSeconds;

        while (!videoFinished)
        {
            // 等待到下一個截圖時間或影片結束
            while (!videoFinished &&
                   videoPlayer.time < nextCaptureTime &&
                   videoPlayer.isPlaying)
            {
                yield return null;
            }

            if (videoFinished)
                break;

            // 到時間點 → 暫停
            videoPlayer.Pause();
            yield return null; // 等一幀確保畫面穩定

            Debug.Log($"Capture at {videoPlayer.time:F2} sec");

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
                        $"t{videoPlayer.time:F2}_x{pitch:+0;-0}_y{yaw:000}.png";

                    string path = Path.Combine(sessionDir, filename);
                    File.WriteAllBytes(path, tex.EncodeToPNG());

                    yield return new WaitForSeconds(delayBetweenShots);
                }
            }

            nextCaptureTime += captureIntervalSeconds;

            if (!videoFinished)
                videoPlayer.Play();
        }

        // === 結束清理 ===
        captureCamera.targetTexture = null;
        RenderTexture.active = null;

        Destroy(rt);
        Destroy(tex);

        captureCamera.transform.localRotation = Quaternion.identity;

        videoPlayer.Stop();  // 確保不會再重播

        Debug.Log("Video capture completed. All processes stopped.");
    }
}