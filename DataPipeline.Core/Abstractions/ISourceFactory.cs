using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resuelve un StepDefinition de tipo Source a su conector concreto.</summary>
public interface ISourceFactory
{
    ISource Create(StepDefinition step);
}
