using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;

public class SimpleVideoFrameCapture : MonoBehaviour
{
    [Header("Video")]
    public VideoPlayer videoPlayer;

    [Header("Video Source")]
    [Tooltip("影片資料夾（絕對路徑）")]
    public string videoFolderPath;

    [Header("Display")]
    public RawImage targetRawImage;

    [Tooltip("VideoPlayer 必須輸出到此 RenderTexture")]
    private RenderTexture renderTexture;

    [Tooltip("每播放幾秒截圖一次")]
    public float captureIntervalSeconds = 2f;

    [Header("Output")]
    public string outputFolderName = "VideoFrameOutput";

    [Tooltip("暫停後等待多久再截圖")]
    public float delayBetweenShots = 0.05f;

    [Header("Crop Settings")]
    public bool enableSquareCrop = false;

    [Tooltip("是否先旋轉再裁切（建議開啟）")]
    public bool cropAfterRotation = true;

    public enum RotationMode
    {
        None,
        Clockwise90,
        CounterClockwise90
    }

    [Header("Rotation")]
    public RotationMode rotationMode = RotationMode.Clockwise90;

    private bool videoFinished = false;
    private int captureIndex = 0;

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
        videoFinished = true;
        Debug.Log("Video reached end.");
    }

    void Start()
    {
        if (videoPlayer == null)
        {
            Debug.LogError("尚未指定 VideoPlayer");
            return;
        }

        if (string.IsNullOrEmpty(videoFolderPath))
        {
            Debug.LogError("尚未指定影片資料夾路徑");
            return;
        }

        if (!Directory.Exists(videoFolderPath))
        {
            Debug.LogError("資料夾不存在: " + videoFolderPath);
            return;
        }

        // 確保 VideoPlayer 使用 RenderTexture 模式
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;

        StartCoroutine(ProcessAllVideos());
    }
    IEnumerator ProcessAllVideos()
    {
        string[] files = Directory.GetFiles(videoFolderPath, "*.*");
        Array.Sort(files);

        // 新增：批次資料夾
        string rootDir = Path.Combine(Application.dataPath, outputFolderName);
        Directory.CreateDirectory(rootDir);

        string batchName = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string batchDir = Path.Combine(rootDir, batchName);
        Directory.CreateDirectory(batchDir);


        foreach (string file in files)
        {
            if (!IsVideoFile(file))
                continue;

            Debug.Log("處理影片: " + file);

            yield return StartCoroutine(ProcessSingleVideo(file, batchDir));
        }

        Debug.Log("全部影片處理完成");
    }
    bool IsVideoFile(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        return ext == ".mp4" || ext == ".mov" || ext == ".avi" || ext == ".mkv";
    }
    IEnumerator ProcessSingleVideo(string videoPath, string batchDir)
    {
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = videoPath;

        videoPlayer.Stop();
        videoFinished = false;
        captureIndex = 0;

        videoPlayer.Prepare();
        yield return new WaitUntil(() => videoPlayer.isPrepared);

        if (videoPlayer.texture == null)
        {
            Debug.LogError("texture 為 null: " + videoPath);
            yield break;
        }

        int width = videoPlayer.texture.width;
        int height = videoPlayer.texture.height;

        if (width == 0 || height == 0)
        {
            Debug.LogError("解析度錯誤: " + videoPath);
            yield break;
        }

        // 重建 RT
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);


        renderTexture.antiAliasing = 1;
        renderTexture.filterMode = FilterMode.Point;
        renderTexture.useMipMap = false;
        renderTexture.autoGenerateMips = false;

        renderTexture.Create();

        videoPlayer.targetTexture = renderTexture;

        if (targetRawImage != null)
            targetRawImage.texture = renderTexture;

        // 每個影片一個資料夾（很重要）
        string videoName = Path.GetFileNameWithoutExtension(videoPath);

        string sessionDir = Path.Combine(batchDir, videoName);
        Directory.CreateDirectory(sessionDir);

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        videoPlayer.Play();

        double nextCaptureTime = captureIntervalSeconds;

        while (!videoFinished)
        {
            while (!videoFinished &&
                   videoPlayer.time < nextCaptureTime &&
                   videoPlayer.isPlaying)
            {
                yield return null;
            }

            if (videoFinished)
                break;

            videoPlayer.Pause();
            yield return new WaitForSeconds(delayBetweenShots);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;

            captureIndex++;

            string filename = $"frame_{captureIndex:0000}.png";
            string path = Path.Combine(sessionDir, filename);

            Texture2D processed = ProcessTexture(tex);
            File.WriteAllBytes(path, processed.EncodeToPNG());

            if (processed != tex)
                Destroy(processed);

            nextCaptureTime += captureIntervalSeconds;

            if (!videoFinished)
                videoPlayer.Play();
        }

        videoPlayer.Stop();
        Destroy(tex);

        Debug.Log("完成影片: " + videoName);
    }

    Texture2D Rotate90Clockwise(Texture2D src)
    {
        int w = src.width;
        int h = src.height;

        Texture2D dst = new Texture2D(h, w, src.format, false);

        Color[] srcPixels = src.GetPixels();
        Color[] dstPixels = new Color[srcPixels.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dstPixels[(w - x - 1) * h + y] = srcPixels[y * w + x];
            }
        }

        dst.SetPixels(dstPixels);
        dst.Apply();

        return dst;
    }
    Texture2D Rotate90CounterClockwise(Texture2D src)
    {
        int w = src.width;
        int h = src.height;

        Texture2D dst = new Texture2D(h, w, src.format, false);

        Color[] srcPixels = src.GetPixels();
        Color[] dstPixels = new Color[srcPixels.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                dstPixels[x * h + (h - y - 1)] = srcPixels[y * w + x];
            }
        }

        dst.SetPixels(dstPixels);
        dst.Apply();

        return dst;
    }
    Texture2D RotateTexture(Texture2D src)
    {
        switch (rotationMode)
        {
            case RotationMode.Clockwise90:
                return Rotate90Clockwise(src);

            case RotationMode.CounterClockwise90:
                return Rotate90CounterClockwise(src);

            default:
                return src;
        }
    }
    Texture2D ProcessTexture(Texture2D src)
    {
        Texture2D result = src;

        if (cropAfterRotation)
        {
            result = RotateTexture(result);

            if (enableSquareCrop)
                result = CropToSquare(result);
        }
        else
        {
            if (enableSquareCrop)
                result = CropToSquare(result);

            result = RotateTexture(result);
        }

        return result;
    }
    Texture2D CropToSquare(Texture2D src)
    {
        int size = Mathf.Min(src.width, src.height);

        int startX = (src.width - size) / 2;
        int startY = (src.height - size) / 2;

        Color[] pixels = src.GetPixels(startX, startY, size, size);

        Texture2D cropped = new Texture2D(size, size, src.format, false);
        cropped.SetPixels(pixels);
        cropped.Apply();

        return cropped;
    }
    void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}