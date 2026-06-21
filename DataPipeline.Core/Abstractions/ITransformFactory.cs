using DataPipeline.Core.Pipelines;

namespace DataPipeline.Core.Abstractions;

/// <summary>Resuelve un StepDefinition de tipo Transformacion a su transform concreto.</summary>
public interface ITransformFactory
{
    ITransform Create(StepDefinition step);
}
