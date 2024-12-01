﻿using atompds.Pds.AccountManager.Db;
using atompds.Pds.Config;
using atompds.Utils;
using CommonWeb;
using FishyFlip.Lexicon.Com.Atproto.Server;
using FishyFlip.Models;
using Identity;
using Microsoft.AspNetCore.Mvc;
using Xrpc;

namespace atompds.Controllers.Xrpc.Com.Atproto.Server;

[ApiController]
[Route("xrpc")]
public class CreateSessionController : ControllerBase
{
    private readonly Pds.AccountManager.AccountManager _accountManager;
    private readonly IdentityConfig _identityConfig;
    private readonly DidResolver _didResolver;
    private readonly ILogger<CreateSessionController> _logger;

    public CreateSessionController(Pds.AccountManager.AccountManager accountManager,
        IdentityConfig identityConfig, DidResolver didResolver,
        ILogger<CreateSessionController> logger)
    {
        _accountManager = accountManager;
        _identityConfig = identityConfig;
        _didResolver = didResolver;
        _logger = logger;
    }
    
    [HttpPost("com.atproto.server.createSession")]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionInput request)
    {
        if (request.Identifier == null || request.Password == null)
        {
            throw new XRPCError(new InvalidRequestErrorDetail("Identifier and password are required"));
        }
        
        var login = await _accountManager.Login(request.Identifier, request.Password);
        var creds = await _accountManager.CreateSession(login.Did);
        var didDoc = await DidDocForSession(login.Did);
        var (active, status) = FormatAccountStatus(login);
        
        return Ok(new CreateSessionOutput(creds.AccessJwt,
            creds.RefreshJwt,
            new ATHandle(login.Handle ?? Constants.INVALID_HANDLE),
            new ATDid(login.Did),
            didDoc?.ToDidDoc(),
            login.Email,
            login.EmailConfirmedAt != null,
            null,
            active,
            status.ToString()));
    }

    private (bool Active, AccountStore.AccountStatus Status) FormatAccountStatus(ActorAccount? account)
    {
        if (account == null)
        {
            return (false, AccountStore.AccountStatus.Deleted);
        }

        if (account.TakedownRef != null)
        {
            return (false, AccountStore.AccountStatus.Takendown);
        }
        
        if (account.DeactivatedAt != null)
        {
            return (false, AccountStore.AccountStatus.Deactivated);
        }
        
        return (true, AccountStore.AccountStatus.Active);
    }
    
    private async Task<DidDocument?> DidDocForSession(string did, bool forceRefresh = false)
    {
        if (!_identityConfig.EnableDidDocWithSession) return null;
        return await SafeResolveDidDoc(did, forceRefresh);
    }
    
    private async Task<DidDocument?> SafeResolveDidDoc(string did, bool forceRefresh = false)
    {
        try
        {
            var didDoc = await _didResolver.Resolve(did, forceRefresh);
            return didDoc;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to resolve did doc: {did}", did);
            return null;
        }
    }
}