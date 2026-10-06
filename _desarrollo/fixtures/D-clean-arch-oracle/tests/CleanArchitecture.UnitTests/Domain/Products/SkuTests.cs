using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Products;

namespace CleanArchitecture.UnitTests.Domain.Products;

/// <summary>Tests del value object Sku.</summary>
public class SkuTests
{
    [Theory]
    [InlineData("TEC-0001")]
    [InlineData("MON-9999")]
    [InlineData("ABC-0000")]
    public void Create_ConFormatoValido_CreaElValueObject(string value)
    {
        Assert.Equal(value, Sku.Create(value).Value);
    }

    [Fact]
    public void Create_NormalizaMinusculasYEspacios()
    {
        Assert.Equal("TEC-0001", Sku.Create("  tec-0001  ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_SinValor_LanzaDomainException(string? value)
    {
        Assert.Equal("Sku.Required", Assert.Throws<DomainException>(() => Sku.Create(value)).Code);
    }

    [Theory]
    [InlineData("TEC0001")]   // falta el guion
    [InlineData("TE-0001")]   // solo 2 letras
    [InlineData("TECL-0001")] // 4 letras
    [InlineData("TEC-001")]   // solo 3 dígitos
    [InlineData("TEC-00001")] // 5 dígitos
    [InlineData("123-ABCD")]  // partes invertidas
    [InlineData("TEC_0001")]  // separador incorrecto
    [InlineData("TEC-0001 X")]
    public void Create_ConFormatoInvalido_LanzaDomainException(string value)
    {
        Assert.Equal("Sku.InvalidFormat", Assert.Throws<DomainException>(() => Sku.Create(value)).Code);
    }

    [Fact]
    public void Equals_MismoValorTrasNormalizar_SonIguales()
    {
        Assert.Equal(Sku.Create("TEC-0001"), Sku.Create("tec-0001"));
    }
}
