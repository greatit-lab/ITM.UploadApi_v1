// ITM.UploadApi_v1/Controllers/FileUploadController.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ITM.UploadApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        private readonly string _baseStoragePath;
        private readonly string[] _allowedExtensions;

        public FileUploadController(IConfiguration configuration)
        {
            _baseStoragePath = configuration["AppSettings:SavePath"] ?? "/appdata/object_store";
            var extSettings = configuration["AppSettings:AllowedExtensions"] ?? ".pdf,.jpg,.png,.txt";
            _allowedExtensions = extSettings.Split(',').Select(x => x.Trim().ToLower()).ToArray();
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok("ItmUploadApi is healthy.");
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string sdwt, [FromForm] string eqpid)
        {
            if (file == null || file.Length == 0) return BadRequest(new { message = "업로드할 파일이 없습니다." });

            if (string.IsNullOrWhiteSpace(sdwt)) sdwt = "Unknown_SDWT";
            if (string.IsNullOrWhiteSpace(eqpid)) eqpid = "Unknown_EQPID";

            try
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedExtensions.Contains(extension)) return BadRequest(new { message = $"허용되지 않는 파일 형식입니다: {extension}" });

                string dateFolder;
                var safeOriginalName = Path.GetFileName(file.FileName);
                var dateMatch = Regex.Match(safeOriginalName, @"^(\d{8})");

                if (dateMatch.Success) dateFolder = dateMatch.Groups[1].Value;
                else dateFolder = DateTime.Now.ToString("yyyyMMdd");

                var targetDirectory = Path.Combine(_baseStoragePath, sdwt, eqpid, dateFolder);

                if (!Directory.Exists(targetDirectory)) Directory.CreateDirectory(targetDirectory);

                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(safeOriginalName);
                var uniqueFileName = $"{fileNameWithoutExt}_{Guid.NewGuid()}{extension}";
                var finalFilePath = Path.Combine(targetDirectory, uniqueFileName);

                using (var stream = new FileStream(finalFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var referenceAddress = $"/{sdwt}/{eqpid}/{dateFolder}/{uniqueFileName}";

                return Ok(new { message = "파일 업로드 완료", fileName = uniqueFileName, path = finalFilePath, referenceAddress = referenceAddress, size = file.Length });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"서버 오류 발생: {ex.Message}" });
            }
        }

        [HttpGet("download")]
        public IActionResult DownloadFile([FromQuery] string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return BadRequest("파일 경로가 필요합니다.");

            try
            {
                var cleanPath = relativePath.TrimStart('/', '\\');
                var fullPath = Path.Combine(_baseStoragePath, cleanPath);

                if (!System.IO.File.Exists(fullPath)) return NotFound("요청한 파일을 찾을 수 없습니다.");

                var fileBytes = System.IO.File.ReadAllBytes(fullPath);
                var fileName = Path.GetFileName(fullPath);

                string contentType = "application/octet-stream";
                if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) contentType = "application/pdf";
                else if (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) contentType = "image/jpeg";
                else if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) contentType = "image/png";

                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"다운로드 오류: {ex.Message}");
            }
        }

        [HttpGet("size")]
        public IActionResult GetStorageSize()
        {
            try
            {
                if (!Directory.Exists(_baseStoragePath)) return Ok(new { success = true, sizeBytes = 0, message = "Directory not found." });
                long totalSizeBytes = ITM.UploadApi.Services.StorageSizeMonitorService.CachedSizeBytes;
                return Ok(new { success = true, sizeBytes = totalSizeBytes, message = "Returned instantly" });
            }
            catch (Exception ex) { return StatusCode(500, new { success = false, sizeBytes = 0, message = ex.Message }); }
        }

        // =========================================================================
        // [초고속 최적화] 특정 날짜(YYYYMMDD) 폴더만 다이렉트로 스캔하여 타임아웃 방지
        // =========================================================================
        [HttpGet("daily-size")]
        public IActionResult GetDailyStorageSize([FromQuery] string date)
        {
            if (string.IsNullOrEmpty(date)) return BadRequest(new { success = false, message = "Date is required" });

            try
            {
                if (!Directory.Exists(_baseStoragePath)) return Ok(new { success = true, sizeBytes = 0 });

                long dailySizeBytes = 0;

                // 디렉토리 구조: _baseStoragePath / sdwt / eqpid / yyyyMMdd
                var sdwtDirs = Directory.EnumerateDirectories(_baseStoragePath);
                foreach (var sdwt in sdwtDirs)
                {
                    var eqpDirs = Directory.EnumerateDirectories(sdwt);
                    foreach (var eqp in eqpDirs)
                    {
                        var targetDateDir = Path.Combine(eqp, date);
                        if (Directory.Exists(targetDateDir))
                        {
                            var dirInfo = new DirectoryInfo(targetDateDir);
                            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                dailySizeBytes += file.Length;
                            }
                        }
                    }
                }

                return Ok(new { success = true, sizeBytes = dailySizeBytes });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, sizeBytes = 0, message = ex.Message });
            }
        }
    }
}
