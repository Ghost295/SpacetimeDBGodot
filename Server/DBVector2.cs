using System;

[SpacetimeDB.Type]
public partial struct DBVector2
{
    public float x;
    public float y;

    public DBVector2(float x, float y)
    {
        this.x = x;
        this.y = y;
    }

    public float SqrMagnitude => x * x + y * y;

    public float Magnitude => MathF.Sqrt(SqrMagnitude);

    public DBVector2 Normalized => this / Magnitude;

    public static DBVector2 operator +(DBVector2 a, DBVector2 b) => new DBVector2(a.x + b.x, a.y + b.y);
    public static DBVector2 operator -(DBVector2 a, DBVector2 b) => new DBVector2(a.x - b.x, a.y - b.y);
    public static DBVector2 operator *(DBVector2 a, float b) => new DBVector2(a.x * b, a.y * b);
    public static DBVector2 operator /(DBVector2 a, float b) => new DBVector2(a.x / b, a.y / b);
}
