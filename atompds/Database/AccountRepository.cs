﻿using System.Text.Json.Serialization;
using atompds.Enc;
using atompds.Model;
using atompds.Utils;
using FishyFlip.Lexicon.Com.Atproto.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace atompds.Database;

public class AccountRepository
{
    private readonly DataContext _db;
    private readonly PasswordHasher<string> _hasher;

    public AccountRepository(DataContext db)
    {
        _db = db;
        _hasher = new PasswordHasher<string>();
    }
    
    public async Task<AccountInfo> CreateAccountAsync(CreateAccountInput request)
    {
        var handleStr = request.Handle?.Handle;
        if (string.IsNullOrEmpty(handleStr))
        {
            throw new Exception("handle is required");
        }
        
        var passwordStr = request.Password;
        if (string.IsNullOrEmpty(passwordStr))
        {
            throw new Exception("password is required");
        }
        
        var didStr = request.Did?.Handler;
        if (!string.IsNullOrEmpty(didStr))
        {
            throw new Exception("DID is not allowed to be set");
        }
        
        var existingAccount = await GetAccountRecord(handleStr);
        if (existingAccount != null)
        {
            throw new DuplicateAccountException("account already exists");
        }

        var keyPair = DidKeyGenerator.GenerateKeyPair();
        var (privateKey, publicKey) = DidKeyGenerator.ToHexStrings(keyPair);

        var accDid = $"did:web:{request.Handle}";
        var account = new AccountRecord(accDid, handleStr, _hasher.HashPassword(accDid, passwordStr), privateKey, publicKey);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return new AccountInfo(account.Did, account.Handle);
    }
    
    private async Task<AccountRecord?> GetAccountRecord(string identifier)
    {
        return await _db.Accounts
            .FirstOrDefaultAsync(x => x.Did == identifier || x.Handle == identifier);
    }
    
    public async Task<AccountInfo?> GetAccountAsync(string identifier)
    {
        var result = await GetAccountRecord(identifier);

        if (result != null)
        {
            return new AccountInfo(result.Did, result.Handle);
        }
        
        return null;
    }
    
    public async Task<string?> HandleByDidAsync(string did)
    {
        var result = await _db.Accounts
            .FirstOrDefaultAsync(x => x.Did == did);
        return result?.Handle;
    }
    
    public async Task<string?> DidByHandleAsync(string handle)
    {
        var result = await _db.Accounts
            .FirstOrDefaultAsync(x => x.Handle == handle);
        return result?.Did;
    }
    
    public async Task<(string did, string handle)> VerifyAccountLoginAsync(string identifier, string password)
    {
        var result = await GetAccountRecord(identifier);
        if (result == null)
        {
            throw new Exception("no account found");
        }

        var verificationResult = _hasher.VerifyHashedPassword(result.Did, result.Password, password);
        if (verificationResult != PasswordVerificationResult.Success)
        {
            throw new Exception("invalid password");
        }

        return (result.Did, result.Handle);
    }
    
    public async Task<AccountInfo[]> GetAccountsAsync(string[] actors)
    {
        var result = await _db.Accounts
            .Where(x => actors.Contains(x.Did) || actors.Contains(x.Handle))
            .ToListAsync();
        return result.Select(x => new AccountInfo(x.Did, x.Handle)).ToArray();
    }

    public record AccountInfo(
        [property: JsonPropertyName("did")] string Did,
        [property: JsonPropertyName("handle")] string Handle);


}

public class DuplicateAccountException : Exception
{
    public DuplicateAccountException(string message) : base(message)
    {
    }
}