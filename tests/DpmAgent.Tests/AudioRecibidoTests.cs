using DpmAgent.Api.Voz;

namespace DpmAgent.Tests;

public class AudioRecibidoTests
{
    private static readonly TranscripcionOptions Limites = new() { MaxBytes = 1024 * 1024, MaxDuracionSegundos = 120 };

    [Fact]
    public void Mide_la_duracion_de_un_ogg_opus_de_whatsapp()
    {
        var audio = AudioRecibido.Validar(AudiosDePrueba.OggOpus(12.5), Limites);

        Assert.Equal(FormatoAudio.Ogg, audio.Formato);
        Assert.Equal(12.5, audio.DuracionSegundos!.Value, precision: 3);
        Assert.Equal("audio/ogg", audio.ContentType);
    }

    [Fact]
    public void Mide_la_duracion_de_un_wav()
    {
        var audio = AudioRecibido.Validar(AudiosDePrueba.Wav(3), Limites);

        Assert.Equal(FormatoAudio.Wav, audio.Formato);
        Assert.Equal(3, audio.DuracionSegundos!.Value, precision: 3);
    }

    [Fact]
    public void Acepta_webm_del_navegador_sin_duracion_medible()
    {
        var audio = AudioRecibido.Validar(AudiosDePrueba.WebM(), Limites);

        Assert.Equal(FormatoAudio.WebM, audio.Formato);
        Assert.Null(audio.DuracionSegundos);
    }

    [Theory]
    [InlineData(new byte[] { 0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0, 0, 0 }, FormatoAudio.Mp3)]
    [InlineData(new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0 }, FormatoAudio.Mp3)]
    [InlineData(new byte[] { 0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20 }, FormatoAudio.Mp4)]
    public void Detecta_mp3_y_m4a(byte[] datos, FormatoAudio esperado)
    {
        Assert.Equal(esperado, AudioRecibido.Validar(datos, Limites).Formato);
    }

    [Fact]
    public void Rechaza_un_archivo_que_no_es_audio_aunque_diga_que_lo_es()
    {
        var pdf = "%PDF-1.7 hola mundo"u8.ToArray();

        var ex = Assert.Throws<AudioInvalidoException>(() => AudioRecibido.Validar(pdf, Limites));
        Assert.Contains("no soportado", ex.Message);
    }

    [Fact]
    public void Rechaza_audio_vacio()
    {
        Assert.Throws<AudioInvalidoException>(() => AudioRecibido.Validar([], Limites));
    }

    [Fact]
    public void Rechaza_audio_mas_grande_que_el_limite()
    {
        var limites = new TranscripcionOptions { MaxBytes = 100 };

        var ex = Assert.Throws<AudioInvalidoException>(() => AudioRecibido.Validar(AudiosDePrueba.Wav(1), limites));
        Assert.Contains("límite", ex.Message);
    }

    [Fact]
    public void Rechaza_audio_mas_largo_que_el_limite()
    {
        var ex = Assert.Throws<AudioInvalidoException>(() => AudioRecibido.Validar(AudiosDePrueba.OggOpus(121), Limites));
        Assert.Contains("120 segundos", ex.Message);
    }

    [Fact]
    public void Un_ogg_truncado_no_rompe_la_validacion()
    {
        var truncado = AudiosDePrueba.OggOpus(5)[..30];

        var audio = AudioRecibido.Validar(truncado, Limites);
        Assert.Equal(FormatoAudio.Ogg, audio.Formato);
        Assert.Null(audio.DuracionSegundos);
    }
}
