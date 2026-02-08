using Microsoft.AspNetCore.Mvc;

namespace SmartGlassesMiddleware.Controllers;

[ApiController]
[Route("api/debug")]
public class DebugController : ControllerBase
{
    [HttpPost("echo-form")]
    public async Task<IActionResult> EchoForm()
    {
        try
        {
            if (!Request.HasFormContentType)
            {
                return BadRequest(new { error = "Not a form content type" });
            }

            var form = await Request.ReadFormAsync();
            var fields = form.Keys.ToDictionary(k => k, k => form[k].ToString());
            var files = form.Files.Select(f => new { f.FileName, f.Name, f.Length, f.ContentType }).ToList();
            return Ok(new { fields, filesCount = files.Count, files });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("echo-raw")]
    public async Task<IActionResult> EchoRaw()
    {
        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms);
            return Ok(new { length = ms.Length });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
