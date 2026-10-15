using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Net;
#if ISB_SPT41
using CatalogDatabase = SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable;
using CatalogLocales = SPTarkov.Server.Core.Services.Locales.LocaleService;
#else
using CatalogDatabase = SPTarkov.Server.Core.Services.DatabaseService;
using CatalogLocales = SPTarkov.Server.Core.Services.LocaleService;
#endif
using System.Reflection;
using System.Text.Json;
using SPTarkov.Server.Core.Helpers;

#if ISB_SPT41
using Microsoft.AspNetCore.Authorization;
using SPTarkov.Server.Core.Helpers.Server;
#endif
namespace ISBSpecialForces.Web;
#if ISB_SPT41

[Authorize(Policy = "Administrator")]
#endif
#if ISB_FINAL_BETA
[NonController]
#else
[ApiController, Route("isb")]
#endif
public sealed class IsbWebController : Controller
{
    private readonly IsbConfigFiles _files;
    public IsbWebController(ModHelper helper)
    {
        var folder = helper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _files = new IsbConfigFiles(folder, FindGameRoot(folder), IsbConfigValidation.Validate);
    }

    internal static string? FindGameRoot(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "BepInEx")))
                return dir.FullName;
        return null;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
#if !ISB_SPT41
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote != null && !IPAddress.IsLoopback(remote) && !remote.Equals(HttpContext.Connection.LocalIpAddress))
        {
            context.Result = StatusCode(403, new { message = "On SPT 4.0, open this panel on the server computer." });
            return;
        }

#endif
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public IActionResult Home() => Resource("index.html", "text/html; charset=utf-8");
    [HttpGet("assets/{name}")]
    public IActionResult Assets(string name) => name switch
    {
        "app.js" => Resource(name, "application/javascript; charset=utf-8"),
        "style.css" => Resource(name, "text/css; charset=utf-8"),
        "isb-logo.png" => Resource(name, "image/png"),
        _ => NotFound()};
    private IActionResult Resource(string file, string contentType)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ISBSpecialForces.Web." + file);
        if (stream == null)
            return NotFound();
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return File(bytes.ToArray(), contentType);
    }

    [HttpGet("api/catalog")]
    public IActionResult Catalog([FromServices] IAntiforgery antiforgery, [FromServices] CatalogDatabase database, [FromServices] CatalogLocales locales)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
#if ISB_SPT41
        var items = database.Items.Values;
#else
        var items = database.GetTemplates().Items.Values;
#endif
        var english = locales.GetLocaleDb("en");
        var available = items.Where(i => i.Properties != null && IsbDogtagCatalog.IsSelectable(i.Id.ToString(), i.Properties.QuestItem)).Select(i => new { value = i.Id.ToString(), label = IsbDogtagCatalog.Label(i.Id.ToString(), i.Name, english.GetValueOrDefault(i.Id + " Name", i.Name ?? i.Id.ToString())), identity = i.Properties!.DogTagQualities == true, parent = i.Parent.ToString() }).Where(i => i.identity || i.parent == "590c745b86f7743cc433c5f2" && (i.label.Contains("dogtag", StringComparison.OrdinalIgnoreCase) || i.label.Contains("dog tag", StringComparison.OrdinalIgnoreCase))).OrderBy(i => i.label, StringComparer.OrdinalIgnoreCase).ToArray();
        var duplicateNames = available.GroupBy(i => i.label, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dogtags = available.Select(i => new { i.value, label = duplicateNames.Contains(i.label) ? i.label + " · " + i.value[^8..] : i.label }).ToArray();
        return Ok(new { documents = _files.Catalog().Select(IsbConfigFiles.Public), botTypes = IsbBotCatalog.All, dogtags, token = tokens.RequestToken, tokenHeader = tokens.HeaderName, 
#if ISB_SPT41
            version = "SPT 4.1.6", launcherLink = true
#else
        version = "SPT 4.0.13", launcherLink = false 
#endif
        });
    }

    [HttpGet("api/document/{id}")]
    public IActionResult Read(string id)
    {
        try
        {
            return Ok(_files.Read(id));
        }
        catch (Exception e)when (e is ArgumentException or JsonException or IOException)
        {
            return BadRequest(new { message = "Could not read this configuration: " + e.Message });
        }
    }

    [HttpPost("api/save"), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Save([FromBody] IsbSaveRequest request, [FromServices] IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return Ok(_files.Save(request));
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new { message = "The session token is invalid or expired. Reload this page before saving." });
        }
        catch (IsbConflictException e)
        {
            return Conflict(new { message = e.Message });
        }
        catch (Exception e)when (e is ArgumentException or JsonException or IOException or UnauthorizedAccessException)
        {
            return BadRequest(new { message = "Could not save: " + e.Message });
        }
    }
}
