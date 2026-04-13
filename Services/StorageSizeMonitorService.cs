// ITM.UploadApi_v1/Services/StorageSizeMonitorService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace ITM.UploadApi.Services
{
    public class StorageSizeMonitorService : BackgroundService
    {
        private readonly string _baseStoragePath;
        
        // 다른 클래스(Controller)에서 즉시 값을 가져갈 수 있도록 static 선언
        public static long CachedSizeBytes { get; private set; } = 0;

        public StorageSizeMonitorService(IConfiguration configuration)
        {
            _baseStoragePath = configuration["AppSettings:SavePath"] ?? "/appdata/object_store";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 서버가 켜져있는 동안 백그라운드에서 무한 반복
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (Directory.Exists(_baseStoragePath))
                    {
                        long totalSize = 0;
                        var dirInfo = new DirectoryInfo(_baseStoragePath);
                        
                        // 하위의 모든 파일을 스캔하여 용량 합산 (백그라운드에서 실행되므로 API 렉 유발 없음)
                        foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            totalSize += file.Length;
                        }
                        
                        // 계산이 완료되면 전역 변수 업데이트
                        CachedSizeBytes = totalSize;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Storage Monitor] Error calculating size: {ex.Message}");
                }

                // 12시간 대기 후 다시 계산 (필요에 따라 TimeSpan.FromHours(24)로 변경 가능)
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
        }
    }
}
