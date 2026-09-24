using Scrapper.Models.Enums;

namespace Scrapper.Services.Interfaces;

public interface IFieldInferenceEngine
{
    /// <summary>Infers the semantic kind of a field from its name alone (e.g. "Consultation Fee" → Currency).</summary>
    FieldKind Infer(string fieldName);
}
