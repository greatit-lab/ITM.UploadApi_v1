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
            // 1. 저장소 기본 경로 로드 (appsettings.json 또는 기본값)
            _baseStoragePath = configuration["AppSettings:SavePath"] ?? "/appdata/object_store";
            
            // 2. 허용 확장자 설정
            var extSettings = configuration["AppSettings:AllowedExtensions"] ?? ".pdf,.jpg,.png,.txt";
            _allowedExtensions = extSettings.Split(',').Select(x => x.Trim().ToLower()).ToArray();
        }

        /// <summary>
        /// 서버 상태 확인 (Health Check)
        /// </summary>
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok("ItmUploadApi is healthy.");
        }

        /// <summary>
        /// 파일 업로드
        /// 저장 구조: {Base}/{sdwt}/{eqpid}/{yyyyMMdd}/{FileName}
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> Upload(
            [FromForm] IFormFile file, 
            [FromForm] string sdwt, 
            [FromForm] string eqpid)
        {
            // 1. 파일 유효성 검사
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "업로드할 파일이 없습니다." });
            }

            // 메타데이터가 비어있을 경우 기본값 처리 (폴더 구조 유지용)
            if (string.IsNullOrWhiteSpace(sdwt)) sdwt = "Unknown_SDWT";
            if (string.IsNullOrWhiteSpace(eqpid)) eqpid = "Unknown_EQPID";

            try
            {
                // 2. 확장자 검사 (소문자 변환 후 비교)
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedExtensions.Contains(extension))
                {
                    return BadRequest(new { message = $"허용되지 않는 파일 형식입니다: {extension}" });
                }

                // 3. 날짜 폴더명 결정
                // (1순위) 파일명 앞 8자리 숫자가 날짜 형식이면 사용
                // (2순위) 없으면 현재 날짜 사용
                string dateFolder;
                var safeOriginalName = Path.GetFileName(file.FileName);
                var dateMatch = Regex.Match(safeOriginalName, @"^(\d{8})");

                if (dateMatch.Success)
                {
                    dateFolder = dateMatch.Groups[1].Value;
                }
                else
                {
                    dateFolder = DateTime.Now.ToString("yyyyMMdd");
                }

                // 4. 저장할 전체 디렉토리 경로 구성 (리눅스 호환 Path.Combine)
                // 예: /appdata/object_store/SDWT01/EQP01/20260211
                var targetDirectory = Path.Combine(_baseStoragePath, sdwt, eqpid, dateFolder);

                // 5. 디렉토리가 없으면 생성 (상위 폴더 포함 재귀적 생성)
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                // 6. 고유 파일명 생성 (중복 방지)
                // 예: 20260211_Report_Guid.pdf
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(safeOriginalName);
                var uniqueFileName = $"{fileNameWithoutExt}_{Guid.NewGuid()}{extension}";

                // 7. 최종 파일 경로
                var finalFilePath = Path.Combine(targetDirectory, uniqueFileName);

                // 8. 파일 저장 실행
                using (var stream = new FileStream(finalFilePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // 9. DB 저장용 웹 접근 경로 생성 (Reference Address)
                // 리눅스 경로 구분자('/') 사용
                var referenceAddress = $"/{sdwt}/{eqpid}/{dateFolder}/{uniqueFileName}";

                return Ok(new 
                { 
                    message = "파일 업로드 및 분류 완료", 
                    fileName = uniqueFileName,
                    path = finalFilePath,
                    referenceAddress = referenceAddress,
                    size = file.Length
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"서버 오류 발생: {ex.Message}" });
            }
        }

        /// <summary>
        /// 파일 다운로드
        /// 요청: /api/FileUpload/download?relativePath=SDWT/EQP/Date/File.pdf
        /// </summary>
        [HttpGet("download")]
        public IActionResult DownloadFile([FromQuery] string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return BadRequest("파일 경로(relativePath)가 필요합니다.");
            }

            try
            {
                // 입력된 상대 경로의 앞쪽 슬래시 제거 후 기본 경로와 결합
                var cleanPath = relativePath.TrimStart('/', '\\');
                var fullPath = Path.Combine(_baseStoragePath, cleanPath);

                if (!System.IO.File.Exists(fullPath))
                {
                    return NotFound("요청한 파일을 찾을 수 없습니다.");
                }

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
    }
}
