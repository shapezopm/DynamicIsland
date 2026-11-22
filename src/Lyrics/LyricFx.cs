namespace DynamicIsland;

// Стиль «живого текста» строки плеера. Off = выключено (поведение по умолчанию не меняется).
enum LyricFx
{
    Off,
    Lift,
    Glow,
    Shimmer,
}

// Смещение в пикселях (X, Y), доля перелива (0..1), свечение (0..1). Искра больше не используется, поля оставлены нулевыми.
readonly record struct LyricFxFrame(double X, double Y, double Shade, double Glow, double Spark, double SparkRise);

// Положение буквы относительно фронта заливки. Front и Center — доли ширины строки (0..1),
// Span — полуширина огибающей, Edge — ширина сглаживания у концов строки.
readonly record struct LyricFxLetter(double Front, double Center, double Span, double Edge, int Index, double Step);

// Чистая математика эффектов: без WPF, покрыта тестами.
// Настроек у эффектов нет: интенсивность и плавность заданы здесь, по одному варианту.
static class LyricFxPlan
{
    public const int StyleCount = 4;

    static readonly string[] StyleNames = ["Выкл", "Нежный подъём", "Свечение и подъём", "Перелив оттенком"];

    // Амплитуды в пикселях при интенсивности 1.
    const double LiftPixels = 1.2, GlowLiftPixels = 2.8;
    const double EdgeMinimum = 0.08, EdgeMaximum = 0.5, SpanMaximum = 0.7, SpanMinimum = 0.01;

    // Ширина мягкого перехода цвета у перелива, доля строки: около 2–3 букв на длинной строке.
    const double ShimmerSoftness = 0.06;

    // Хвост подсветки «Свечения»: длина в буквах за фронтом заливки.
    const double GlowTailLetters = 1.5;
    // Ширина плавного гашения хвоста у конца строки, доля строки.
    const double GlowEndFade = 0.05;

    public static string StyleName(int style) => StyleNames[Math.Clamp(style, 0, StyleCount - 1)];

    // Интенсивность: множитель амплитуд. Для всех эффектов штатная (1).
    public static double StrengthScale(int strength) => Math.Clamp(strength, 0, 2) switch
    {
        0 => 0.5,
        1 => 1,
        _ => 1.6,
    };

    // Положение буквы: front — фронт заливки (0..1), center — центр буквы (0..1), count — число букв в строке.
    public static LyricFxLetter Letter(double front, double center, int count, double tempo, int index)
    {
        int n = Math.Max(1, count);
        double span = Math.Clamp(3.0 / n, SpanMinimum, SpanMaximum);
        double edge = Math.Clamp(Math.Max(1.0 / n, EdgeMinimum) * Math.Max(tempo, 0.1), SpanMinimum, EdgeMaximum);
        return new LyricFxLetter(Math.Clamp(front, 0, 1), center, span, edge, index, 1.0 / n);
    }

    // Огибающая буквы: 1 у фронта, 0 за пределами полуширины и у концов строки.
    public static double Envelope(LyricFxLetter letter)
    {
        double d = (letter.Front - letter.Center) / letter.Span;
        if (Math.Abs(d) >= 1) return 0;
        double bell = Math.Pow(1 - d * d, 3);
        return bell * Smooth(letter.Front / letter.Edge) * Smooth((1 - letter.Front) / letter.Edge);
    }

    public static LyricFxFrame Sample(LyricFx fx, int strength, double time, LyricFxLetter letter, bool active)
    {
        if (fx == LyricFx.Off || !active) return default;

        double scale = StrengthScale(strength);
        double env = Envelope(letter);

        return fx switch
        {
            LyricFx.Lift => new(0, -LiftPixels * scale * env, 0, 0, 0, 0),
            LyricFx.Glow => new(0, -GlowLiftPixels * scale * env, 0, GlowTail(letter), 0, 0),
            // Перелив: буква плавно переходит в цвет полосы трека, когда фронт заливки проходит её центр.
            LyricFx.Shimmer => new(0, 0, Tint(letter), 0, 0, 0),
            _ => default,
        };
    }

    // Хвост подсветки: последняя спетая буква светится полностью, предыдущая около 0.4, дальше подсветки нет.
    // Хвост тянется за фронтом заливки, поэтому гаснет вместе с синхронизацией. После конца строки не светится.
    static double GlowTail(LyricFxLetter letter)
    {
        double behind = (letter.Front - letter.Center) / letter.Step;
        if (behind < 0 || behind >= GlowTailLetters) return 0;
        // У конца строки хвост гаснет плавно, а не обрывается вспышкой.
        double end = Smooth(Math.Clamp((1 - letter.Front) / GlowEndFade, 0, 1));
        return Math.Pow(1 - behind / GlowTailLetters, 0.8) * end;
    }

    static double Tint(LyricFxLetter letter)
    {
        double start = letter.Center - ShimmerSoftness / 2;
        double width = Math.Min(ShimmerSoftness, 1 - start);
        return Smooth((letter.Front - start) / width);
    }

    static double Smooth(double value)
    {
        double t = Math.Clamp(value, 0, 1);
        return t * t * t * (t * (6 * t - 15) + 10);
    }
}
