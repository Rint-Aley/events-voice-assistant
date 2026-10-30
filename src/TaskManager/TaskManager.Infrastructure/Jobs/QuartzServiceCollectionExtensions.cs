using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TaskManager.Application.Abstractions;

namespace TaskManager.Infrastructure.Jobs;

/// <summary>
/// Подключение Quartz 4.x с persistent store в том же Postgres. Application видит только
/// <see cref="IJobScheduler"/>; типы Quartz остаются в Infrastructure.
/// </summary>
public static class QuartzServiceCollectionExtensions
{
    public static IServiceCollection AddTaskManagerQuartz(
        this IServiceCollection services,
        string connectionString,
        Action<QuartzJobSchedulerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new QuartzJobSchedulerOptions();
        configure?.Invoke(options);

        return services.AddTaskManagerQuartz(connectionString, options);
    }

    /// <summary>
    /// Подключение с готовыми настройками. Нужен, когда значения приходят из конфигурации:
    /// свойства <see cref="QuartzJobSchedulerOptions"/> доступны только в инициализаторе.
    /// </summary>
    public static IServiceCollection AddTaskManagerQuartz(
        this IServiceCollection services,
        string connectionString,
        QuartzJobSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<IJobScheduler, QuartzJobScheduler>();
        services.AddSingleton<RecurringJobHostedService>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<RecurringJobHostedService>());

        services.AddQuartz(quartz =>
        {
            quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.ConfigureStore(jobStore =>
                {
                    jobStore.TablePrefix = "qrtz_";
                    jobStore.StoreJobDataAsStrings = true;
                });

                // На чистой базе создаёт таблицы QRTZ_*; существующую схему не меняет.
                store.ProvisionSchema();
            });

            quartz.UseDefaultThreadPool(pool => pool.MaxConcurrency = 10);

            quartz.AddJobType<SttJob>();
            quartz.AddJobType<NluJob>();
            quartz.AddJobType<TtsJob>();
            quartz.AddJobType<ApplyJob>();
            quartz.AddJobType<ReconcilerJob>();

            // Слушатель финализирует только задачи ходов: у системных apply/reconciler
            // нет kind/target_id, и их провал не должен выглядеть как исчерпание попыток.
            quartz.AddTriggerListener<RetryExhaustedListener>(
                GroupMatcher<TriggerKey>.GroupEquals(JobKeys.Group));
        });

        services.AddQuartzHostedService(hosting => hosting.WaitForJobsToComplete = true);

        return services;
    }
}
