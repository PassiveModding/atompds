﻿using AccountManager.Db;
using Common;
using PeterO.Cbor;

namespace Sequencer.Types;

public record AccountEvt : ICborEncodable<AccountEvt>
{
    public required string Did { get; init; }
    public required bool Active { get; init; }
    public required AccountStore.AccountStatus? Status { get; init; }
    
    public CBORObject ToCborObject()
    {
        var cbor = CBORObject.NewMap();
        cbor.Add("did", Did);
        cbor.Add("active", Active);
        if (Status != null)
        {
            cbor.Add("status", Status.ToString()!.ToLower());
        }
        return cbor;
    }
    
    public static AccountEvt FromCborObject(CBORObject cbor)
    {
        var did = cbor["did"].AsString();
        var active = cbor["active"].AsBoolean();
        string? status = null;
        if (cbor.ContainsKey("status"))
        {
            status = cbor["status"].AsString();
        }
            
        return new AccountEvt
        {
            Did = did,
            Active = active,
            Status = status != null ? Enum.Parse<AccountStore.AccountStatus>(status) : null
        };
    }
}