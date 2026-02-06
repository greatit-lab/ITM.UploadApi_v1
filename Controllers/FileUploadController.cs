// ITM.UploadApi_v1/Controllers/FileUploadController.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
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
            // 1. appsettings.json에서 'AppSettings:SavePath' 경로를 읽어옵니다. (없으면 리눅스 기본 경로 사용)
            _baseStoragePath = configuration["AppSettings:SavePath"] ?? "/appdata/object_store";
            
            // 2. 허용된 확장자 목록을 읽어옵니다.
            var extSettings = configuration["AppSettings:AllowedExtensions"] ?? ".pdf,.jpg,.png,.txt";
            _allowedExtensions = extSettings.Split(',').Select(x => x.Trim().ToLower()).ToArray();
        }

        /// <summary>
        /// ITM Agent의 연결 상태 확인(Health Check)을 위한 API입니다.
        /// Agent의 Server Connection 패널에서 Object Storage 항목의 상태를 결정합니다.
        /// </summary>
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            // 이 엔드포인트가 호출되면 Agent의 상태 창에 초록색 불이 들어옵니다.
            return Ok(new { status = "Healthy", time = DateTime.Now });
        }

        /// <summary>
        /// 실제 파일을 서버의 지정된 경로에 업로드하는 API입니다.
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("전송된 파일이 없거나 비어 있습니다.");
            }

            try
            {
                // 3. 파일 확장자 검사 (리눅스 대소문자 구분 대응)
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedExtensions.Contains(extension))
                {
                    return BadRequest($"허용되지 않는 파일 형식입니다: {extension}");
                }

                // 4. 저장 폴더 존재 여부 확인 및 생성 (절대 경로 보장)
                if (!Directory.Exists(_baseStoragePath))
                {
                    Directory.CreateDirectory(_baseStoragePath);
                }

                // 5. 보안을 위해 파일명에서 경로 조작 문자 제거 후 결합
                var safeFileName = Path.GetFileName(file.FileName);
                var fullPath = Path.Combine(_baseStoragePath, safeFileName);

                // 6. 파일 저장 실행
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return Ok(new
                {
                    fileName = safeFileName,
                    path = fullPath,
                    size = file.Length,
                    message = "업로드 완료"
                });
            }
            catch (Exception ex)
            {
                // 권한(Permission) 문제나 디스크 공간 부족 시 에러 반환
                return StatusCode(500, $"서버 오류: {ex.Message}");
            }
        }
    }
}
