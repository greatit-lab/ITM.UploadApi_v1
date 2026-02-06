// ITM.UploadApi_v1/Controllers/FileUploadController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ITM.UploadApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        private readonly string _baseStoragePath;
        private readonly string[] _allowedExtensions;

        // 1. 설정파일(IConfiguration)을 주입받아 경로를 가져옵니다.
        public FileUploadController(IConfiguration configuration)
        {
            // appsettings.json의 "SavePath"를 읽어옴. 없으면 기본값 사용.
            _baseStoragePath = configuration["AppSettings:SavePath"] ?? "/appdata/object_store";
            
            // 허용 확장자 읽기
            var extSettings = configuration["AppSettings:AllowedExtensions"] ?? ".pdf,.jpg,.png";
            _allowedExtensions = extSettings.Split(',');
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("파일이 비어있습니다.");

            try
            {
                // 2. 확장자 보안 검사 (리눅스 파일 시스템은 대소문자 구분하므로 소문자로 통일)
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (Array.IndexOf(_allowedExtensions, extension) < 0)
                {
                    return BadRequest($"허용되지 않는 파일 형식입니다. ({extension})");
                }

                // 3. 저장할 폴더가 실제로 있는지 확인하고 없으면 만듦 (안전장치)
                if (!Directory.Exists(_baseStoragePath))
                {
                    Directory.CreateDirectory(_baseStoragePath);
                }

                // 4. [핵심] 리눅스/윈도우 호환 경로 결합
                // Path.Combine을 쓰면 리눅스에서는 '/'로, 윈도우에서는 '\'로 알아서 합쳐줍니다.
                // 보안을 위해 파일명에서 경로 조작 문자(.., /) 제거
                var safeFileName = Path.GetFileName(file.FileName); 
                var fullPath = Path.Combine(_baseStoragePath, safeFileName);

                // 5. 파일 저장
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return Ok(new { 
                    fileName = safeFileName, 
                    path = fullPath, 
                    size = file.Length,
                    message = "업로드 성공" 
                });
            }
            catch (Exception ex)
            {
                // 권한 문제(Permission denied) 등이 발생하면 로그에 남김
                return StatusCode(500, $"서버 내부 오류: {ex.Message}");
            }
        }
    }
}
