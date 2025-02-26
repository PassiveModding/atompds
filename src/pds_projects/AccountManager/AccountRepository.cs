﻿using AccountManager.Db;
using CID;
using Config;
using Scrypt;
using Xrpc;

namespace AccountManager;

public class AccountRepository
{
    private readonly AccountStore _accountStore;
    private readonly Auth _auth;
    private readonly AccountManagerDb _db;
    private readonly EmailTokenStore _emailTokenStore;
    private readonly InviteStore _inviteStore;
    private readonly PasswordStore _passwordStore;
    private readonly RepoStore _repoStore;
    private readonly SecretsConfig _secretsConfig;
    private readonly ServiceConfig _serviceConfig;
    public AccountRepository(
        ServiceConfig serviceConfig,
        SecretsConfig secretsConfig,
        AccountStore accountStore,
        RepoStore repoStore,
        PasswordStore passwordStore,
        Auth auth,
        InviteStore inviteStore,
        EmailTokenStore emailTokenStore,
        AccountManagerDb db)
    {
        _serviceConfig = serviceConfig;
        _secretsConfig = secretsConfig;
        _accountStore = accountStore;
        _repoStore = repoStore;
        _passwordStore = passwordStore;
        _auth = auth;
        _inviteStore = inviteStore;
        _emailTokenStore = emailTokenStore;
        _db = db;
    }

    public async Task<string?> GetDidForActor(string repo, AvailabilityFlags? flags = null)
    {
        var account = await _accountStore.GetAccount(repo, flags);
        return account?.Did;
    }

    public async Task<(string AccessJwt, string RefreshJwt)> CreateAccount(string did,
        string handle,
        string? email,
        string? password,
        string repoCid,
        string repoRev,
        string? inviteCode,
        bool? deactivated)
    {
        string? passwordScrypt = null;
        if (password != null)
        {
            var enc = new ScryptEncoder();
            passwordScrypt = enc.Encode(password);
        }

        var tokens = _auth.CreateTokens(did, _secretsConfig.JwtSecret, _serviceConfig.Did, Auth.ACCESS_TOKEN_SCOPE);
        var refreshDecoded = _auth.DecodeRefreshTokenUnsafe(tokens.RefreshToken, _secretsConfig.JwtSecret);
        var now = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(inviteCode))
        {
            // ensure invite code is available
            await _inviteStore.EnsureInviteIsAvailable(inviteCode);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _accountStore.RegisterActor(did, handle, deactivated);
        if (email != null && passwordScrypt != null)
        {
            await _accountStore.RegisterAccount(did, email, passwordScrypt);
        }
        await _inviteStore.RecordInviteUse(did, inviteCode, now);
        await _auth.StoreRefreshToken(refreshDecoded, null);
        await _repoStore.UpdateRoot(did, repoCid, repoRev);
        await transaction.CommitAsync();
        await _db.SaveChangesAsync();

        return (tokens.AccessToken, tokens.RefreshToken);
    }

    public async Task DeleteAccount(string did)
    {
        await _accountStore.DeleteAccount(did);
    }

    public async Task EnsureInviteIsAvailable(string code)
    {
        await _inviteStore.EnsureInviteIsAvailable(code);
    }

    public async Task<ActorAccount?> GetAccount(string handleOrDid, AvailabilityFlags? flags = null)
    {
        return await _accountStore.GetAccount(handleOrDid, flags);
    }

    public async Task<ActorAccount?> GetAccountByEmail(string email, AvailabilityFlags? flags = null)
    {
        return await _accountStore.GetAccountByEmail(email, flags);
    }

    public async Task<(string AccessJwt, string RefreshJwt)> CreateSession(string did, string? appPassword = null)
    {
        // TODO: App password support.
        // scope=auth.formatScope(appPassword)
        var tokens = _auth.CreateTokens(did, _secretsConfig.JwtSecret, _serviceConfig.Did, Auth.ACCESS_TOKEN_SCOPE);
        var refreshDecoded = _auth.DecodeRefreshTokenUnsafe(tokens.RefreshToken, _secretsConfig.JwtSecret);
        await _auth.StoreRefreshToken(refreshDecoded, appPassword);
        return (tokens.AccessToken, tokens.RefreshToken);
    }

    public Task<string> CreateEmailToken(string did, EmailToken.EmailTokenPurpose purpose)
    {
        return _emailTokenStore.CreateEmailToken(did, purpose);
    }

    public Task AssertValidEmailToken(string did, string token, EmailToken.EmailTokenPurpose purpose)
    {
        return _emailTokenStore.AssertValidToken(did, token, purpose);
    }

    public async Task UpdateRepoRoot(string did, Cid cid, string rev)
    {
        await _repoStore.UpdateRoot(did, cid.ToString(), rev);
    }

    public Task<bool> VerifyAccountPassword(string did, string appPassword)
    {
        return _passwordStore.VerifyAccountPassword(did, appPassword);
    }

    public async Task<ActorAccount> Login(string identifier, string password)
    {
        var start = DateTime.UtcNow;
        try
        {
            var identifierNormalized = identifier.ToLower();

            ActorAccount? user;
            if (identifierNormalized.Contains("@"))
            {
                user = await GetAccountByEmail(identifierNormalized, new AvailabilityFlags(true, true));
            }
            else
            {
                user = await GetAccount(identifierNormalized, new AvailabilityFlags(true, true));
            }

            if (user == null)
            {
                throw new XRPCError(new AuthRequiredErrorDetail("Invalid username or password"));
            }

            var validAccountPass = await _passwordStore.VerifyAccountPassword(user.Did, password);
            if (!validAccountPass)
            {
                // TODO: App password validation if acc password fails.
                throw new XRPCError(new AuthRequiredErrorDetail("Invalid username or password"));
            }

            if (user.SoftDeleted)
            {
                throw new XRPCError(new AccountTakenDownErrorDetail("Account has been taken down"));
            }

            return user;
        }
        finally
        {
            // mitigate timing attacks
            var delay = Math.Max(0, 350 - (DateTime.UtcNow - start).Milliseconds);
            await Task.Delay(delay);
        }
    }
}