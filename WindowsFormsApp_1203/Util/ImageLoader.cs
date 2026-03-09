using JYVision.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JYVision.Core
{
    //===== 이미지 파일 순차 로더 =====
    public class ImageLoader
    {
        private List<string> _imagePaths = new List<string>();
        private int _currentIndex = 0;
        private int _totalInspected = 0;

        // ✅ 마지막으로 꺼낸 이미지 경로 (저장 시 원본 파일명 사용)
        public string LastImagePath { get; private set; } = "";

        //------- 이미지 목록 로드 -------
        public void LoadImages(string dirPath)
        {
            _imagePaths = Directory.GetFiles(dirPath, "*.*")
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();

            _currentIndex = 0;
            _totalInspected = 0;
            LastImagePath = "";
            SLogger.Write($"이미지 {_imagePaths.Count}장 로드 완료");
        }

        public bool IsLoadedImages() => _imagePaths.Count > 0;
        public int TotalCount => _imagePaths.Count;
        public int RemainingCount => Math.Max(0, _imagePaths.Count - _totalInspected);

        //------- 다음 이미지 경로 반환 -------
        public string GetNextImagePath()
        {
            if (_imagePaths.Count == 0) return "";
            if (_totalInspected >= _imagePaths.Count) return "";

            string path = _imagePaths[_currentIndex];
            _currentIndex = (_currentIndex + 1) % _imagePaths.Count;
            _totalInspected++;
            LastImagePath = path;   // ✅ 항상 마지막 꺼낸 경로 기록

            SLogger.Write($"이미지 [{_totalInspected}/{_imagePaths.Count}]: {Path.GetFileName(path)}");
            return path;
        }

        //------- 카운터 초기화 -------
        public void Reset()
        {
            _currentIndex = 0;
            _totalInspected = 0;
            LastImagePath = "";
            SLogger.Write("ImageLoader 리셋");
        }
    }
}