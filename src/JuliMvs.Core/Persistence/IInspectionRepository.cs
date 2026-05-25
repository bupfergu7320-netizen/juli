using JuliMvs.Core.Inspection;
using JuliMvs.Core.Vision;

namespace JuliMvs.Core.Persistence;

public interface IInspectionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SaveTemplateAsync(PartTemplate template, CancellationToken cancellationToken = default);

    Task<PartTemplate?> LoadLatestTemplateAsync(string productName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartTemplate>> LoadTemplatesAsync(CancellationToken cancellationToken = default);

    Task SaveResultAsync(InspectionResult result, CancellationToken cancellationToken = default);

    Task SaveProductRecipeAsync(string productName, ProductRecipe recipe, CancellationToken cancellationToken = default);

    Task<ProductRecipe?> LoadProductRecipeAsync(string productName, CancellationToken cancellationToken = default);
}
