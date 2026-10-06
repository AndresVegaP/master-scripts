using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.UnitTests.Domain.SharedKernel;

/// <summary>Tests del value object Currency.</summary>
public class CurrencyTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData("  Usd  ")]
    public void Create_NormalizaElCodigo(string code)
    {
        // Tolerante al leer, estricto al guardar.
        Assert.Equal("USD", Currency.Create(code).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_SinCodigo_LanzaDomainException(string? code)
    {
        var exception = Assert.Throws<DomainException>(() => Currency.Create(code));

        Assert.Equal("Currency.Required", exception.Code);
    }

    [Fact]
    public void Create_ConMonedaNoSoportada_LanzaDomainException()
    {
        var exception = Assert.Throws<DomainException>(() => Currency.Create("XYZ"));

        Assert.Equal("Currency.NotSupported", exception.Code);
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("MXN", 2)]
    [InlineData("COP", 2)]
    [InlineData("PEN", 2)]
    [InlineData("CLP", 0)] // el peso chileno no usa decimales
    public void Create_ConoceLosDecimalesDeCadaMoneda(string code, int expectedDecimalPlaces)
    {
        Assert.Equal(expectedDecimalPlaces, Currency.Create(code).DecimalPlaces);
    }

    [Fact]
    public void Equals_MismoCodigo_SonIguales()
    {
        Assert.Equal(Currency.Create("USD"), Currency.Create("usd"));
    }
}
