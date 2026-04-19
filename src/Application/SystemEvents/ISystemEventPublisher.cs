using BinanceBot.Domain.SystemEvents;

namespace BinanceBot.Application.SystemEvents;

/// <summary>
/// ADR-0016 §16.9.3. Paralel persistence hattı — Serilog'a yazılan domain
/// olaylarının aynısı <c>SystemEvents</c> tablosuna da kaydedilir, UI Sistem
/// Olayları sekmesi için. Telemetry path'tir: insert hatası trade pipeline'ını
/// bloke etmez (implementasyon swallow eder, <see cref="global::Microsoft.Extensions.Logging.ILogger"/>'a warn düşer).
/// </summary>
public interface ISystemEventPublisher
{
    /// <summary>
    /// Yeni bir sistem olay satırı yazar. Idempotent değil — aynı anda iki kez
    /// çağrılırsa iki satır oluşur.
    /// </summary>
    /// <param name="type">Olay tipi (enum).</param>
    /// <param name="message">TR kısa cümle — UI'da listelenir, <c>PayloadJson.message</c> alanına gider.</param>
    /// <param name="detailsJson">İsteğe bağlı zengin payload (symbol, price, ratio vb.). <c>PayloadJson.details</c> alanına gömülür.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(
        SystemEventType type,
        string message,
        string? detailsJson = null,
        CancellationToken ct = default);
}
