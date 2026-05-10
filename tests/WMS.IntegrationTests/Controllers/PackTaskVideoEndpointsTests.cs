using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WMS.BLL.Services.Outbound;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Common;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Outbound;
using WMS.Domain.Entities.Outbound;
using WMS.Web.Controllers;
using WMS.Web.Models.Outbound;
using WMS.Web.Services.Outbound;
using WMS.Web.Services.Storage;

namespace WMS.IntegrationTests.Controllers;

// Phase 17 (ADR-009) — Pack video controller tests. Lives in a
// separate file from PackTasksControllerTests because the video
// endpoints have a different mock surface (IPackVideoService with
// stream returns) — keeping them apart avoids ballooning the main
// test file.
public class PackTaskVideoEndpointsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private record Build(
        PackTasksController Controller,
        Mock<IPackVideoService> VideoService,
        Guid CurrentUserId);

    private static Build BuildController()
    {
        var packRepo = new Mock<IPackTaskRepository>();
        var packFactory = new Mock<IPackTaskRepositoryFactory>();
        packFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(packRepo.Object);

        var soRepo = new Mock<ISalesOrderRepository>();
        var soFactory = new Mock<ISalesOrderRepositoryFactory>();
        soFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(soRepo.Object);

        var boxTypeRepo = new Mock<IBoxTypeRepository>();
        boxTypeRepo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LookupItem>());
        var boxTypeFactory = new Mock<IBoxTypeRepositoryFactory>();
        boxTypeFactory.Setup(f => f.For(It.IsAny<Guid>())).Returns(boxTypeRepo.Object);

        var service = new Mock<IPackTaskService>();

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.RequireTenantId()).Returns(TenantId);

        var currentUserId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.UserId).Returns(currentUserId);

        var cancelValidator = new Mock<IValidator<CancelPackTaskViewModel>>();
        cancelValidator.Setup(v => v.ValidateAsync(
                It.IsAny<CancelPackTaskViewModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var videoService = new Mock<IPackVideoService>();

        var ctrl = new PackTasksController(
            packFactory.Object, soFactory.Object, boxTypeFactory.Object, service.Object,
            videoService.Object,
            tenant.Object, currentUser.Object, cancelValidator.Object);

        var tempDataProvider = new Mock<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(new DefaultHttpContext(), tempDataProvider.Object);

        return new Build(ctrl, videoService, currentUserId);
    }

    private static IFormFile FakeFile(string name, string contentType, int sizeBytes)
    {
        var content = new byte[sizeBytes];
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, sizeBytes, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    // ================================================================
    // POST UploadVideo
    // ================================================================

    [Fact]
    public async Task UploadVideo_NoFile_Returns400()
    {
        var b = BuildController();
        var result = await b.Controller.UploadVideo(
            id: Guid.NewGuid(),
            file: null!,
            durationSec: 12,
            ct: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        b.VideoService.Verify(s => s.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadVideo_EmptyFile_Returns400()
    {
        var b = BuildController();
        var file = FakeFile("empty.webm", "video/webm", sizeBytes: 0);

        var result = await b.Controller.UploadVideo(
            id: Guid.NewGuid(), file, durationSec: 0, ct: CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UploadVideo_Happy_ReturnsJsonVideoId()
    {
        var b = BuildController();
        var taskId = Guid.NewGuid();
        var newVideoId = Guid.NewGuid();
        var file = FakeFile("p.webm", "video/webm", sizeBytes: 1024);

        b.VideoService.Setup(s => s.UploadAsync(
                TenantId, taskId, It.IsAny<Stream>(),
                "p.webm", "video/webm", 12, b.CurrentUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(newVideoId);

        var result = await b.Controller.UploadVideo(taskId, file, 12, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        var returnedId = json.Value!.GetType().GetProperty("videoId")!.GetValue(json.Value);
        Assert.Equal(newVideoId, returnedId);
    }

    [Fact]
    public async Task UploadVideo_StorageValidationException_Returns400WithMessage()
    {
        var b = BuildController();
        var file = FakeFile("big.webm", "video/webm", sizeBytes: 1024);
        b.VideoService.Setup(s => s.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorageValidationException("File too large (51.0 MB > 50 MB cap)."));

        var result = await b.Controller.UploadVideo(
            Guid.NewGuid(), file, durationSec: 30, ct: CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("too large", bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value)!.ToString());
    }

    [Fact]
    public async Task UploadVideo_TaskNotPacked_ServiceThrows_Returns400()
    {
        var b = BuildController();
        var file = FakeFile("p.webm", "video/webm", sizeBytes: 1024);
        b.VideoService.Setup(s => s.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Cannot attach video to pack task in 'Pending' state — must be Packed."));

        var result = await b.Controller.UploadVideo(
            Guid.NewGuid(), file, durationSec: 5, ct: CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Pending", bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value)!.ToString());
    }

    // ================================================================
    // GET Video
    // ================================================================

    [Fact]
    public async Task Video_NotFound_Returns404()
    {
        var b = BuildController();
        b.VideoService.Setup(s => s.GetStreamAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Stream Stream, string ContentType, string FileName)?)null);

        var result = await b.Controller.Video(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Video_Happy_ReturnsFileWithContentType()
    {
        var b = BuildController();
        var bytes = new byte[] { 1, 2, 3 };
        var stream = new MemoryStream(bytes);
        b.VideoService.Setup(s => s.GetStreamAsync(
                TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((stream, "video/webm", "pack.webm"));

        var result = await b.Controller.Video(Guid.NewGuid(), CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("video/webm", fileResult.ContentType);
        Assert.Equal("pack.webm", fileResult.FileDownloadName);
        Assert.True(fileResult.EnableRangeProcessing);
    }

    // ================================================================
    // DELETE Video
    // ================================================================

    [Fact]
    public async Task DeleteVideo_NotFound_Returns404()
    {
        var b = BuildController();
        b.VideoService.Setup(s => s.DeleteAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Controller.DeleteVideo(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteVideo_Happy_Returns204()
    {
        var b = BuildController();
        b.VideoService.Setup(s => s.DeleteAsync(
                TenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Controller.DeleteVideo(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }
}
