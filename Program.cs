// ITM.UploadApi_v1/Program.cs
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// 1. 서비스 등록 (컨트롤러, 스웨거 등)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// (선택사항) 폐쇄망 내부 연동을 위해 CORS 전체 허용 (Agent나 Web 서버 접속용)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// 2. HTTP 파이프라인 설정
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

// -----------------------------------------------------------------------------
// [핵심 수정 부분] 리눅스 절대 경로 에러 해결 로직
// -----------------------------------------------------------------------------

// (1) 설정 파일(appsettings.json)에서 경로 읽기. (못 읽으면 기본값 사용)
var configPath = builder.Configuration["AppSettings:SavePath"];
var finalStoragePath = string.IsNullOrWhiteSpace(configPath) 
    ? "/appdata/object_store"  // 기본값
    : configPath;              // 설정값

// (2) 폴더가 실제로 없으면 생성 (안전장치)
if (!Directory.Exists(finalStoragePath))
{
    try
    {
        Directory.CreateDirectory(finalStoragePath);
        Console.WriteLine($"[Info] 저장소 폴더를 생성했습니다: {finalStoragePath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Error] 폴더 생성 실패 (권한 확인 필요): {ex.Message}");
    }
}

// (3) 정적 파일 미들웨어 설정 (UseStaticFiles)
// 중요: Path.GetFullPath()를 사용하여 무조건 절대 경로로 변환하여 에러 방지
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(finalStoragePath)),
    RequestPath = "/files" // 예: http://서버IP:8082/files/이미지.jpg 로 접근 가능
});

// -----------------------------------------------------------------------------

app.UseAuthorization();

app.MapControllers();

app.Run();
