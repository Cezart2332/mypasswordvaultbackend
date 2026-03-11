using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyPasswordVault.API.DTOs.Vault;
using MyPasswordVault.API.Services.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace MyPasswordVault.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class VaultController : ControllerBase
{
    private readonly IVaultService _vaultService;

    public VaultController(IVaultService vaultService)
    {
        _vaultService = vaultService;
    }

    [HttpGet("items")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> GetItems()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var items = await _vaultService.GetVaultItems(userId);
        return Ok(items);
    }

    [HttpPost("items")]
    [EnableRateLimiting("vault")]
    [RequestSizeLimit(65_536)]
    public async Task<IActionResult> AddItem([FromBody] VaultEntryRequestDto request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var item = await _vaultService.AddVaultItem(userId, request);
        return Ok(item);
    }

    [HttpDelete("items/{id}")]
    [EnableRateLimiting("vault")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _vaultService.DeleteVaultItem(userId, id);
        return NoContent();
    }

    [HttpPut("items/{id}")]
    [EnableRateLimiting("vault")]
    [RequestSizeLimit(65_536)]
    public async Task<IActionResult> EditItem(int id,[FromBody] VaultEntryRequestDto request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var item = await _vaultService.EditVaultItems(userId,id, request);
        return Ok(item);
    }
}
