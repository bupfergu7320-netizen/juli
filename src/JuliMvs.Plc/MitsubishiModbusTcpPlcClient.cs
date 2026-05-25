using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using JuliMvs.Core.Inspection;
using JuliMvs.Core.Plc;

namespace JuliMvs.Plc;

public sealed record PlcOutputReadback(
    int Trigger,
    float XDeviation,
    float YDeviation,
    float RDeviation,
    int ResultCode);

public sealed class MitsubishiModbusTcpPlcClient : IPlcClient
{
    private const byte ReadHoldingRegistersFunction = 0x03;
    private const byte WriteSingleRegisterFunction = 0x06;
    private const byte WriteMultipleRegistersFunction = 0x10;

    private readonly MitsubishiModbusTcpOptions _options;
    private readonly PlcOutputTransform _outputTransform;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _transactionId;

    public MitsubishiModbusTcpPlcClient(MitsubishiModbusTcpOptions? options = null)
    {
        _options = options ?? MitsubishiModbusTcpOptions.Default;
        _outputTransform = _options.OutputTransform ?? PlcOutputTransform.Identity;
    }

    public bool IsConnected => _client?.Connected == true && _stream is not null;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await CloseAsync(cancellationToken);

        var client = new TcpClient
        {
            ReceiveTimeout = _options.ReadWriteTimeoutMilliseconds,
            SendTimeout = _options.ReadWriteTimeoutMilliseconds
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ConnectTimeoutMilliseconds);
        await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);

        _client = client;
        _stream = client.GetStream();
    }

    public async Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var triggerRegisters = await ReadHoldingRegistersAsync(_options.TriggerAddress, 1, cancellationToken);
        var trigger = triggerRegisters[0];
        var (targetProduction, currentProduction) = await ReadProductionCountersAsync(cancellationToken);
        var productModel = await ReadProductModelAsync(cancellationToken);

        return new PlcSnapshot(
            IsRunning: IsConnected,
            WorkpieceInPosition: trigger != 0,
            CaptureRequested: trigger == 1,
            ProductModel: productModel,
            TargetProduction: targetProduction,
            CurrentProduction: currentProduction,
            AlarmCode: null);
    }

    public async Task WriteInspectionResultAsync(
        InspectionResult result,
        CancellationToken cancellationToken = default)
    {
        var measurement = result.Measurement;
        var resultCode = result.Decision == InspectionDecision.Ok && measurement is not null
            ? 1
            : 2;

        if (resultCode == 1)
        {
            await WriteOkDeviationOutputAsync(measurement!, cancellationToken);
        }
        else
        {
            await WriteZeroDeviationOutputAsync(cancellationToken);
            await WriteSingleRegisterAsync(_options.ResultAddress, 2, cancellationToken);
        }

        // Standard handshake: PC writes the result first; caller clears D1000 after handoff.
    }

    public Task ClearTriggerAsync(CancellationToken cancellationToken = default)
    {
        return WriteSingleRegisterAsync(_options.TriggerAddress, 0, cancellationToken);
    }

    public Task ClearResultCodeAsync(CancellationToken cancellationToken = default)
    {
        return WriteSingleRegisterAsync(_options.ResultAddress, 0, cancellationToken);
    }

    public async Task<PlcOutputReadback> ReadOutputReadbackAsync(CancellationToken cancellationToken = default)
    {
        var triggerRegisters = await ReadHoldingRegistersAsync(_options.TriggerAddress, 1, cancellationToken);
        var outputRegisters = await ReadHoldingRegistersAsync(
            _options.XCompensationAddress,
            CheckedQuantity(_options.ResultAddress - _options.XCompensationAddress + 1),
            cancellationToken);

        var xIndex = _options.XCompensationAddress - _options.XCompensationAddress;
        var yIndex = _options.YCompensationAddress - _options.XCompensationAddress;
        var rIndex = _options.RCompensationAddress - _options.XCompensationAddress;
        var resultIndex = _options.ResultAddress - _options.XCompensationAddress;

        return new PlcOutputReadback(
            Trigger: triggerRegisters[0],
            XDeviation: RegistersToFloat(outputRegisters[xIndex], outputRegisters[xIndex + 1], _options.SwapFloatWords),
            YDeviation: RegistersToFloat(outputRegisters[yIndex], outputRegisters[yIndex + 1], _options.SwapFloatWords),
            RDeviation: RegistersToFloat(outputRegisters[rIndex], outputRegisters[rIndex + 1], _options.SwapFloatWords),
            ResultCode: outputRegisters[resultIndex]);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _sync.WaitAsync(cancellationToken);
        try
        {
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _sync.Dispose();
    }

    internal static ushort[] FloatToRegisters(float value, bool swapWords)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);

        var highWord = BinaryPrimitives.ReadUInt16BigEndian(bytes[..2]);
        var lowWord = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]);
        return swapWords
            ? [lowWord, highWord]
            : [highWord, lowWord];
    }

    public static float RoundDeviationForPlc(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return Math.Abs(rounded) < 0.005
            ? 0.0f
            : (float)rounded;
    }

    internal static float RegistersToFloat(ushort firstRegister, ushort secondRegister, bool swapWords)
    {
        var highWord = swapWords ? secondRegister : firstRegister;
        var lowWord = swapWords ? firstRegister : secondRegister;

        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(bytes[..2], highWord);
        BinaryPrimitives.WriteUInt16BigEndian(bytes[2..], lowWord);
        return BinaryPrimitives.ReadSingleBigEndian(bytes);
    }

    private async Task<(int TargetProduction, int CurrentProduction)> ReadProductionCountersAsync(
        CancellationToken cancellationToken)
    {
        var startAddress = Math.Min(_options.TargetProductionAddress, _options.CurrentProductionAddress);
        var endAddress = Math.Max(_options.TargetProductionAddress, _options.CurrentProductionAddress);
        var quantity = CheckedQuantity(endAddress - startAddress + 1);

        var registers = await ReadHoldingRegistersAsync(startAddress, quantity, cancellationToken);
        var targetProduction = registers[_options.TargetProductionAddress - startAddress];
        var currentProduction = registers[_options.CurrentProductionAddress - startAddress];

        return (targetProduction, currentProduction);
    }

    private async Task<string> ReadProductModelAsync(CancellationToken cancellationToken)
    {
        if (_options.ProductModelRegisterCount <= 0)
        {
            return string.Empty;
        }

        var registers = await ReadHoldingRegistersAsync(
            _options.ProductModelAddress,
            CheckedQuantity(_options.ProductModelRegisterCount),
            cancellationToken);

        return RegistersToAsciiString(registers);
    }

    private static string RegistersToAsciiString(IReadOnlyList<ushort> registers)
    {
        if (registers.Count == 0)
        {
            return string.Empty;
        }

        var bytes = new byte[registers.Count * 2];
        for (var i = 0; i < registers.Count; i++)
        {
            bytes[i * 2] = (byte)(registers[i] >> 8);
            bytes[i * 2 + 1] = (byte)(registers[i] & 0xFF);
        }

        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes, 0, length).TrimEnd();
    }

    private async Task<PlcOutputCommand> WriteOkDeviationOutputAsync(
        InspectionMeasurement measurement,
        CancellationToken cancellationToken)
    {
        var output = PlcInspectionOutputCalculator.CalculateFinalCorrection(measurement, _outputTransform);
        var x = RoundDeviationForPlc(output.XDeviation);
        var y = RoundDeviationForPlc(output.YDeviation);
        var r = RoundDeviationForPlc(output.RDeviation);

        await WriteDeviationOutputAsync(x, y, r, cancellationToken);
        await WriteSingleRegisterAsync(_options.ResultAddress, 1, cancellationToken);

        return new PlcOutputCommand(x, y, r);
    }

    private async Task WriteZeroDeviationOutputAsync(CancellationToken cancellationToken)
    {
        await WriteDeviationOutputAsync(0.0f, 0.0f, 0.0f, cancellationToken);
    }

    private async Task WriteDeviationOutputAsync(
        float x,
        float y,
        float r,
        CancellationToken cancellationToken)
    {
        if (_options.YCompensationAddress == _options.XCompensationAddress + 2 &&
            _options.RCompensationAddress == _options.XCompensationAddress + 4)
        {
            var registers = FloatToRegisters(x, _options.SwapFloatWords)
                .Concat(FloatToRegisters(y, _options.SwapFloatWords))
                .Concat(FloatToRegisters(r, _options.SwapFloatWords))
                .ToArray();
            await WriteMultipleRegistersAsync(_options.XCompensationAddress, registers, cancellationToken);
            return;
        }

        await WriteFloatAsync(_options.XCompensationAddress, x, cancellationToken);
        await WriteFloatAsync(_options.YCompensationAddress, y, cancellationToken);
        await WriteFloatAsync(_options.RCompensationAddress, r, cancellationToken);
    }

    private Task WriteFloatAsync(
        int address,
        float value,
        CancellationToken cancellationToken)
    {
        var registers = FloatToRegisters(value, _options.SwapFloatWords);
        return WriteMultipleRegistersAsync(address, registers, cancellationToken);
    }

    private async Task WriteSingleRegisterAsync(
        int address,
        ushort value,
        CancellationToken cancellationToken)
    {
        var pdu = new byte[5];
        pdu[0] = WriteSingleRegisterFunction;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), CheckedAddress(address));
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), value);

        var response = await SendRequestAsync(pdu, cancellationToken);
        if (response.Length != 5 || response[0] != WriteSingleRegisterFunction)
        {
            throw new InvalidOperationException("PLC写单个保持寄存器响应格式错误。");
        }

        var responseAddress = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(1, 2));
        var responseValue = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(3, 2));
        if (responseAddress != CheckedAddress(address) || responseValue != value)
        {
            throw new InvalidOperationException("PLC写单个保持寄存器响应地址或数值不匹配。");
        }
    }

    private async Task<ushort[]> ReadHoldingRegistersAsync(
        int startAddress,
        ushort quantity,
        CancellationToken cancellationToken)
    {
        if (quantity == 0)
        {
            return [];
        }

        var pdu = new byte[5];
        pdu[0] = ReadHoldingRegistersFunction;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), CheckedAddress(startAddress));
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), quantity);

        var response = await SendRequestAsync(pdu, cancellationToken);
        if (response.Length < 2 || response[0] != ReadHoldingRegistersFunction)
        {
            throw new InvalidOperationException("PLC读取保持寄存器响应格式错误。");
        }

        var byteCount = response[1];
        if (byteCount != quantity * 2 || response.Length != byteCount + 2)
        {
            throw new InvalidOperationException("PLC读取保持寄存器响应长度错误。");
        }

        var registers = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
        {
            registers[i] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2 + i * 2, 2));
        }

        return registers;
    }

    private async Task WriteMultipleRegistersAsync(
        int startAddress,
        IReadOnlyList<ushort> values,
        CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return;
        }

        if (values.Count > 123)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "单次写入保持寄存器数量不能超过123。");
        }

        var pdu = new byte[6 + values.Count * 2];
        pdu[0] = WriteMultipleRegistersFunction;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1, 2), CheckedAddress(startAddress));
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3, 2), (ushort)values.Count);
        pdu[5] = checked((byte)(values.Count * 2));
        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(6 + i * 2, 2), values[i]);
        }

        var response = await SendRequestAsync(pdu, cancellationToken);
        if (response.Length != 5 || response[0] != WriteMultipleRegistersFunction)
        {
            throw new InvalidOperationException("PLC写保持寄存器响应格式错误。");
        }

        var responseAddress = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(1, 2));
        var responseQuantity = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(3, 2));
        if (responseAddress != CheckedAddress(startAddress) || responseQuantity != values.Count)
        {
            throw new InvalidOperationException("PLC写保持寄存器响应地址或数量不匹配。");
        }
    }

    private async Task<byte[]> SendRequestAsync(byte[] pdu, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            var stream = EnsureStream();
            var request = BuildAdu(pdu);
            await stream.WriteAsync(request, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var header = new byte[7];
            await ReadExactlyAsync(stream, header, cancellationToken);
            var transactionId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2));
            var protocolId = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
            var remainingLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            var unitId = header[6];

            if (transactionId != _transactionId || protocolId != 0 || unitId != _options.UnitId)
            {
                throw new InvalidOperationException("PLC响应头与请求不匹配。");
            }

            if (remainingLength < 2)
            {
                throw new InvalidOperationException("PLC响应长度错误。");
            }

            var responsePdu = new byte[remainingLength - 1];
            await ReadExactlyAsync(stream, responsePdu, cancellationToken);
            if (responsePdu.Length > 0 && (responsePdu[0] & 0x80) != 0)
            {
                var exceptionCode = responsePdu.Length > 1 ? responsePdu[1] : 0;
                throw new InvalidOperationException($"PLC返回Modbus异常码: {exceptionCode}。");
            }

            return responsePdu;
        }
        catch
        {
            MarkDisconnected();
            throw;
        }
        finally
        {
            _sync.Release();
        }
    }

    private byte[] BuildAdu(byte[] pdu)
    {
        _transactionId++;
        var request = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), _transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), checked((ushort)(pdu.Length + 1)));
        request[6] = _options.UnitId;
        pdu.CopyTo(request.AsSpan(7));
        return request;
    }

    private NetworkStream EnsureStream()
    {
        if (_stream is null || _client is null || !_client.Connected)
        {
            throw new InvalidOperationException("PLC未连接。");
        }

        return _stream;
    }

    private void MarkDisconnected()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("PLC连接已关闭。");
            }

            offset += read;
        }
    }

    private static ushort CheckedAddress(int address)
    {
        if (address is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Modbus保持寄存器地址必须在0到65535之间。");
        }

        return (ushort)address;
    }

    private static ushort CheckedQuantity(int quantity)
    {
        if (quantity is < 0 or > 125)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Modbus保持寄存器读取数量必须在0到125之间。");
        }

        return (ushort)quantity;
    }
}
