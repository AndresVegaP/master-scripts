namespace CleanArchitecture.Application.Products.GetProductById;

/// <summary>Pregunta por un producto concreto.</summary>
/// <param name="ProductId">Identidad del producto buscado.</param>
public sealed record GetProductByIdQuery(Guid ProductId);

// Una QUERY es la contraparte de lectura de un comando: representa una pregunta
// al sistema y garantiza que no cambia nada.
//
// ¿Vale la pena un record para envolver un solo Guid? Sí, por consistencia y
// por evolución: todos los casos de uso reciben su objeto de entrada, y si
// mañana la consulta acepta más parámetros, se agregan aquí sin cambiar la
// firma del handler ni de quien lo llama.
