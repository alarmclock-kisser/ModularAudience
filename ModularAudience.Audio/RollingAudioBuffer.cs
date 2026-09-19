internal sealed class RollingAudioBuffer
{
    private readonly byte[] buffer;
    private int writePosition;
    private int count;

    public RollingAudioBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.buffer = new byte[capacity];
    }

    public int Count => this.count;

    public void Append(byte[] source, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (offset < 0 || length < 0 || offset > source.Length - length)
        {
            throw new ArgumentOutOfRangeException();
        }

        if (length == 0)
        {
            return;
        }

        if (length >= this.buffer.Length)
        {
            offset += length - this.buffer.Length;
            length = this.buffer.Length;
        }

        int firstLength = Math.Min(length, this.buffer.Length - this.writePosition);
        Buffer.BlockCopy(source, offset, this.buffer, this.writePosition, firstLength);
        int remainingLength = length - firstLength;
        if (remainingLength > 0)
        {
            Buffer.BlockCopy(source, offset + firstLength, this.buffer, 0, remainingLength);
        }

        this.writePosition = (this.writePosition + length) % this.buffer.Length;
        this.count = Math.Min(this.buffer.Length, this.count + length);
    }

    public byte[] GetLast(int length)
    {
        length = Math.Clamp(length, 0, this.count);
        byte[] result = new byte[length];
        if (length == 0)
        {
            return result;
        }

        int start = (this.writePosition - length + this.buffer.Length) % this.buffer.Length;
        int firstLength = Math.Min(length, this.buffer.Length - start);
        Buffer.BlockCopy(this.buffer, start, result, 0, firstLength);
        int remainingLength = length - firstLength;
        if (remainingLength > 0)
        {
            Buffer.BlockCopy(this.buffer, 0, result, firstLength, remainingLength);
        }

        return result;
    }

    public void Clear()
    {
        Array.Clear(this.buffer);
        this.writePosition = 0;
        this.count = 0;
    }
}