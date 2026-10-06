using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using CleanArchitecture.Application.Products.CreateProduct;
using CleanArchitecture.Domain.Products;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CleanArchitecture.ArchitectureTests;

/// <summary>Las mismas reglas, con ArchUnitNET (nivel 2).</summary>
public class LayerDependencyTests
{
    // 📘 docs/02-clean-architecture.md
    //
    // ArchUnitNET lee el código compilado y permite escribir reglas que se leen
    // casi como una frase:
    //
    //     Types().That().Are(Domain).Should().NotDependOnAny(Infrastructure)
    //
    // Frente a la reflexión a mano del nivel 1, gana cuando las reglas dejan de
    // ser "qué ensamblado referencia a cuál" y pasan a ser "qué TIPO usa a cuál"
    // o convenciones de nombres y estructura.
    //
    // DETALLE QUE CONFUNDE LA PRIMERA VEZ: una regla como "X no debe depender de
    // Y" se cumple sola si X no tiene ningún tipo (por ejemplo, porque el
    // nombre del ensamblado está mal escrito). Para evitar esa falsa sensación
    // de seguridad, ArchUnitNET exige que las reglas negativas declaren
    // explícitamente WithoutRequiringPositiveResults(). Por eso, además, el
    // nivel 1 comprueba con reflexión que las capas existen y se referencian
    // como esperamos: las dos redes juntas no dejan agujeros.
    //
    // OTRA TRAMPA REAL, y por eso está documentada aquí: identificar una capa
    // con ResideInAssembly("CleanArchitecture.Domain") NO funciona, porque esa
    // comparación se hace contra el nombre COMPLETO del ensamblado (incluye
    // versión y token). El conjunto quedaba vacío y las reglas pasaban sin
    // comprobar nada. Por eso identificamos cada capa con su Assembly real.

    // Cargar la arquitectura una sola vez: es lo más caro de estos tests.
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Product).Assembly,
            typeof(CreateProductHandler).Assembly,
            typeof(Infrastructure.DependencyInjection).Assembly,
            typeof(Program).Assembly)
        .Build();

    private static readonly IObjectProvider<IType> Domain =
        Types().That().ResideInAssembly(typeof(Product).Assembly).As("la capa Domain");

    private static readonly IObjectProvider<IType> Application =
        Types().That().ResideInAssembly(typeof(CreateProductHandler).Assembly).As("la capa Application");

    private static readonly IObjectProvider<IType> Infrastructure =
        Types().That().ResideInAssembly(typeof(Infrastructure.DependencyInjection).Assembly).As("la capa Infrastructure");

    private static readonly IObjectProvider<IType> Api =
        Types().That().ResideInAssembly(typeof(Program).Assembly).As("la capa Api");

    [Fact]
    public void Domain_NoDependeDeNingunaCapaExterior() =>
        Types().That().Are(Domain).Should()
            .NotDependOnAny(Application).AndShould()
            .NotDependOnAny(Infrastructure).AndShould()
            .NotDependOnAny(Api)
            .Because("el dominio es el centro: todas las flechas apuntan hacia él")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    [Fact]
    public void Application_NoDependeDeInfrastructureNiDeLaApi() =>
        Types().That().Are(Application).Should()
            .NotDependOnAny(Infrastructure).AndShould()
            .NotDependOnAny(Api)
            .Because("los casos de uso hablan con puertos, no con adaptadores")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    [Fact]
    public void Infrastructure_NoDependeDeLaApi() =>
        Types().That().Are(Infrastructure).Should()
            .NotDependOnAny(Api)
            .Because("los detalles técnicos no saben quién los usa")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    [Fact]
    public void LosContratosHttp_NoUsanTiposDelDominio() =>
        Types().That().ResideInNamespace("CleanArchitecture.Api.Products").Or()
            .ResideInNamespace("CleanArchitecture.Api.Orders").Should()
            .NotDependOnAny(Domain)
            .Because("el contrato público de la API no debe filtrar entidades ni value objects")
            .WithoutRequiringPositiveResults()
            .Check(Architecture);

    [Fact]
    public void LosCasosDeUso_SonClasesSelladas() =>
        Classes().That().HaveNameEndingWith("Handler").And().Are(Application).And().AreNotAbstract().Should()
            .BeSealed()
            .Because("un caso de uso no está pensado para heredarse")
            .Check(Architecture);

    [Fact]
    public void LosRepositorios_SonClasesSelladasEnInfrastructure() =>
        Classes().That().HaveNameEndingWith("Repository").Should()
            .BeSealed().AndShould()
            .Be(Infrastructure)
            .Because("los adaptadores de persistencia viven en la capa más externa")
            .Check(Architecture);

    [Fact]
    public void LosSnapshots_VivenEnElDominio() =>
        Types().That().HaveNameEndingWith("Snapshot").Should()
            .Be(Domain)
            .Because("el snapshot es el contrato que publica el propio agregado")
            .Check(Architecture);
}
