using DocumentIA.Batch.ClassificationLite.Services;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class KeyObfuscatorTests
{
    private const string SampleKey = "SuperSecretFunctionKey==123";

    [Fact]
    public void Protect_LuegoUnprotect_RecuperaElOriginal()
    {
        var protectedValue = KeyObfuscator.Protect(SampleKey);

        var recovered = KeyObfuscator.Unprotect(protectedValue);

        Assert.Equal(SampleKey, recovered);
    }

    [Fact]
    public void Protect_DevuelveValorConPrefijoYDistintoDelOriginal()
    {
        var protectedValue = KeyObfuscator.Protect(SampleKey);

        Assert.StartsWith(KeyObfuscator.Prefix, protectedValue);
        Assert.NotEqual(SampleKey, protectedValue);
    }

    [Fact]
    public void Protect_DosVeces_ProduceSalidasDistintas_PeroAmbasDescifran()
    {
        var first = KeyObfuscator.Protect(SampleKey);
        var second = KeyObfuscator.Protect(SampleKey);

        Assert.NotEqual(first, second);
        Assert.Equal(SampleKey, KeyObfuscator.Unprotect(first));
        Assert.Equal(SampleKey, KeyObfuscator.Unprotect(second));
    }

    [Fact]
    public void Unprotect_DeTextoEnClaro_LoDevuelveIgual()
    {
        var recovered = KeyObfuscator.Unprotect(SampleKey);

        Assert.Equal(SampleKey, recovered);
    }

    [Fact]
    public void Protect_DeVacioYNull_DevuelveVacio()
    {
        Assert.Equal(string.Empty, KeyObfuscator.Protect(string.Empty));
        Assert.Equal(string.Empty, KeyObfuscator.Protect(null));
    }

    [Fact]
    public void Unprotect_DeVacio_DevuelveVacio()
    {
        Assert.Equal(string.Empty, KeyObfuscator.Unprotect(string.Empty));
        Assert.Equal(string.Empty, KeyObfuscator.Unprotect(null));
    }

    [Fact]
    public void Protect_DeValorYaCifrado_NoLoDobleCifra()
    {
        var protectedOnce = KeyObfuscator.Protect(SampleKey);

        var protectedTwice = KeyObfuscator.Protect(protectedOnce);

        Assert.Equal(protectedOnce, protectedTwice);
    }

    [Fact]
    public void Unprotect_DeBlobCorrupto_DevuelveVacioSinLanzar()
    {
        var recovered = KeyObfuscator.Unprotect(KeyObfuscator.Prefix + "no-es-base64-valido!!!");

        Assert.Equal(string.Empty, recovered);
    }
}
