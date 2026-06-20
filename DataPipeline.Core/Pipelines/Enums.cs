namespace DataPipeline.Core.Pipelines;

public enum StepKind { Source, Transform }
public enum ScheduleKind { OnDemand, Daily, Weekly, Monthly }
public enum WriteMode { Append, Upsert, Replace }
