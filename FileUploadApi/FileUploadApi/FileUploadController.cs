using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FileUploadApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileUploadController : ControllerBase
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileUploadController> _logger;

    public FileUploadController(
        BlobServiceClient blobServiceClient,
        IConfiguration configuration,
        ILogger<FileUploadController> logger)
    {
        _blobServiceClient = blobServiceClient;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(104857600)] // 100MB
    public async Task<IActionResult> UploadFiles([FromForm] List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { message = "No files were uploaded." });
        }

        var containerName = _configuration["AzureStorage:ContainerName"] ?? "uploads";
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        
        // Create container if it doesn't exist
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var uploadResults = new List<UploadResult>();

        foreach (var file in files)
        {
            try
            {
                if (file.Length == 0)
                {
                    uploadResults.Add(new UploadResult
                    {
                        FileName = file.FileName,
                        Success = false,
                        Message = "File is empty"
                    });
                    continue;
                }

                // Generate unique filename
                var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                var blobClient = containerClient.GetBlobClient(fileName);

                // Set content type
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = file.ContentType
                };

                // Upload file
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobUploadOptions
                    {
                        HttpHeaders = blobHttpHeaders
                    });
                }

                uploadResults.Add(new UploadResult
                {
                    FileName = file.FileName,
                    Success = true,
                    Message = "File uploaded successfully",
                    BlobUrl = blobClient.Uri.ToString(),
                    BlobName = fileName
                });

                _logger.LogInformation($"File {file.FileName} uploaded successfully as {fileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file {file.FileName}");
                uploadResults.Add(new UploadResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        var allSuccess = uploadResults.All(r => r.Success);
        var statusCode = allSuccess ? 200 : 207; // 207 Multi-Status for partial success

        return StatusCode(statusCode, new
        {
            message = $"{uploadResults.Count(r => r.Success)} of {uploadResults.Count} files uploaded successfully",
            results = uploadResults
        });
    }

    [HttpGet("test")]
    public IActionResult TestConnection()
    {
        try
        {
            var containerName = _configuration["AzureStorage:ContainerName"] ?? "uploads";
            return Ok(new { message = "API is running", containerName });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }
}

public class UploadResult
{
    public string FileName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BlobUrl { get; set; }
    public string? BlobName { get; set; }
}