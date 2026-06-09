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

    [Tooltip("影片資料夾路徑（會依序處理資料夾內所有影片）")]
    public string videoFolderPath;

    [Tooltip("VideoPlayer 輸出用的 RenderTexture，截圖流程開始前會自動套用至 videoPlayer.targetTexture")]
    public RenderTexture targetRenderTexture;

    [Tooltip("�C����X���I�Ϥ@��")]
    public float captureIntervalSeconds = 2f;

    [Header("Output")]
    public int resolution = 1024;
    public float fieldOfView = 60f;

    [Tooltip("截圖輸出根目錄（絕對路徑）")]
    public string outputRootPath;

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
            throw new Exception("�|�����w captureCamera");

        if (videoPlayer == null)
            throw new Exception("�|�����w videoPlayer");

        if (string.IsNullOrEmpty(videoFolderPath) || !Directory.Exists(videoFolderPath))
        {
            Debug.LogWarning($"[PanoramaVideoCaptureTool] videoFolderPath 無效或不存在: \"{videoFolderPath}\"，截圖程序取消。");
            return;
        }

        if (string.IsNullOrEmpty(outputRootPath) || !Directory.Exists(outputRootPath))
        {
            Debug.LogWarning($"[PanoramaVideoCaptureTool] outputRootPath 無效或不存在: \"{outputRootPath}\"，截圖程序取消。");
            return;
        }

        if (targetRenderTexture == null)
        {
            Debug.LogWarning("[PanoramaVideoCaptureTool] targetRenderTexture 為空，截圖程序取消。");
            return;
        }

        string[] allFiles = Directory.GetFiles(videoFolderPath, "*.*");
        bool hasVideo = false;
        foreach (string f in allFiles)
        {
            if (IsVideoFile(f)) { hasVideo = true; break; }
        }
        if (!hasVideo)
        {
            Debug.LogWarning($"[PanoramaVideoCaptureTool] videoFolderPath 內找不到任何影片檔案，截圖程序取消。");
            return;
        }

        SetupCamera();

        List<float> pitchAngles;
        List<float> yawAngles;

        GetSamplingAngles(out pitchAngles, out yawAngles);

        videoPlayer.targetTexture = targetRenderTexture;

        StartCoroutine(ProcessAllVideos(pitchAngles, yawAngles));
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
                throw new Exception("Custom mode �ҥΡA�� customPitchAngles ����");

            if (customYawAngles == null || customYawAngles.Length == 0)
                throw new Exception("Custom mode �ҥΡA�� customYawAngles ����");

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
            throw new Exception($"Pitch �]�w���~�G180 �L�k�Q (pitchGroupCount + 1) = {pitchDivisor} �㰣");

        if (360 % yawGroupCount != 0)
            throw new Exception($"Yaw �]�w���~�G360 �L�k�Q yawGroupCount = {yawGroupCount} �㰣");
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

    bool IsVideoFile(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv";
    }

    IEnumerator ProcessAllVideos(List<float> pitchAngles, List<float> yawAngles)
    {
        string[] files = Directory.GetFiles(videoFolderPath, "*.*");
        Array.Sort(files);

        string rootDir = outputRootPath;
        Directory.CreateDirectory(rootDir);

        string batchName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string batchDir = Path.Combine(rootDir, batchName);
        Directory.CreateDirectory(batchDir);

        Debug.Log($"Panorama video capture output folder: {batchDir}");

        foreach (string file in files)
        {
            if (!IsVideoFile(file))
                continue;

            Debug.Log("處理影片: " + file);
            yield return StartCoroutine(CaptureFromVideo(file, batchDir, pitchAngles, yawAngles));
        }

        Debug.Log("所有影片處理完成");
    }

    IEnumerator CaptureFromVideo(string videoPath, string batchDir, List<float> pitchAngles, List<float> yawAngles)
    {
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = videoPath;
        videoPlayer.Stop();
        videoFinished = false;

        videoPlayer.Prepare();
        while (!videoPlayer.isPrepared)
            yield return null;

        string videoName = Path.GetFileNameWithoutExtension(videoPath);
        string sessionDir = Path.Combine(batchDir, videoName);
        Directory.CreateDirectory(sessionDir);

        RenderTexture rt = new RenderTexture(resolution, resolution, 24);
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);

        captureCamera.targetTexture = rt;

        videoPlayer.Play();

        double nextCaptureTime = captureIntervalSeconds;

        while (!videoFinished)
        {
            // ���ݨ�U�@�ӺI�Ϯɶ��μv������
            while (!videoFinished &&
                   videoPlayer.time < nextCaptureTime &&
                   videoPlayer.isPlaying)
            {
                yield return null;
            }

            if (videoFinished)
                break;

            // ��ɶ��I �� �Ȱ�
            videoPlayer.Pause();
            yield return null; // ���@�V�T�O�e��í�w

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

        // === �����M�z ===
        captureCamera.targetTexture = null;
        RenderTexture.active = null;

        Destroy(rt);
        Destroy(tex);

        captureCamera.transform.localRotation = Quaternion.identity;

        videoPlayer.Stop();  // �T�O���|�A����

        Debug.Log($"完成影片: {videoName}");
    }
}
