using FluentValidation;
using Scrapper.Models.DTOs;
using Scrapper.Utils.Constants;

namespace Scrapper.Api.Validators;

public class ValidateUrlRequestDtoValidator : AbstractValidator<ValidateUrlRequestDto>
{
    public ValidateUrlRequestDtoValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(2048);
    }
}

public class FieldDefinitionDtoValidator : AbstractValidator<FieldDefinitionDto>
{
    public FieldDefinitionDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        // Selector is optional — Advanced Options only. When omitted, automatic
        // multi-strategy extraction is used instead (see FieldExtractionOrchestrator).
        RuleFor(x => x.Selector).MaximumLength(500);

        RuleFor(x => x.Attribute)
            .NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.Selector) && x.ExtractionType == Models.Enums.ExtractionType.Attribute)
            .WithMessage("Attribute name is required when extraction type is Attribute.");
    }
}

public class ScraperConfigDtoValidator : AbstractValidator<ScraperConfigDto>
{
    public ScraperConfigDtoValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Fields).NotEmpty().WithMessage("At least one field must be configured.");
        RuleFor(x => x.Fields).Must(f => f.Count <= ScrapingLimits.MaxFields)
            .WithMessage($"No more than {ScrapingLimits.MaxFields} fields may be configured.");
        RuleFor(x => x.Fields)
            .Must(fields => fields
                .Select(f => f.Name.Trim().ToLowerInvariant())
                .Distinct()
                .Count() == fields.Count)
            .WithMessage("Field names must be unique.");
        RuleForEach(x => x.Fields).SetValidator(new FieldDefinitionDtoValidator());
        RuleFor(x => x.MaxRecords).InclusiveBetween(1, ScrapingLimits.MaxRecordsHardCap);
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(ScrapingLimits.MinTimeoutSeconds, ScrapingLimits.MaxTimeoutSeconds);
        RuleFor(x => x.MaxProfiles).InclusiveBetween(0, ScrapingLimits.MaxProfilesHardCap);
        RuleFor(x => x.MaxPages).InclusiveBetween(1, ScrapingLimits.MaxPagesHardCap);
    }
}

public class TestSelectorRequestDtoValidator : AbstractValidator<TestSelectorRequestDto>
{
    public TestSelectorRequestDtoValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Selector).NotEmpty().MaximumLength(500);
    }
}

public class ExportRequestDtoValidator : AbstractValidator<ExportRequestDto>
{
    public ExportRequestDtoValidator()
    {
        RuleFor(x => x.FieldNames).NotEmpty();
    }
}
