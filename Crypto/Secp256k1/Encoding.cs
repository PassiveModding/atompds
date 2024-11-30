﻿using Secp256k1Net;

namespace Crypto.Secp256k1;

public class Encoding
{
    private static readonly Secp256k1Net.Secp256k1 Secp256K1 = new Secp256k1Net.Secp256k1();
    
    public static byte[] CompressPubKey(byte[] pubKeyBytes)
    {
        var compactOutput = new byte[Secp256k1Net.Secp256k1.SERIALIZED_COMPRESSED_PUBKEY_LENGTH];
        if (!Secp256K1.PublicKeySerialize(compactOutput, pubKeyBytes, Flags.SECP256K1_EC_COMPRESSED))
        {
            throw new Exception("Failed to compress public key");
        }
        
        return compactOutput;
    }
    
    public static byte[] DecompressPubKey(byte[] pubKeyBytes)
    {
        var decompressedOutput = new byte[Secp256k1Net.Secp256k1.SERIALIZED_UNCOMPRESSED_PUBKEY_LENGTH];
        if (!Secp256K1.PublicKeyParse(decompressedOutput, pubKeyBytes))
        {
            throw new Exception("Failed to decompress public key");
        }
        
        return decompressedOutput;
    }
}