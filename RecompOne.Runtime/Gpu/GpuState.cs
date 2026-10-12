using System.Runtime.InteropServices;
using RecompOne.Runtime.Hle;

namespace RecompOne.Runtime;

public sealed partial class Gpu
{
    const int StateTile = 64;

    /// <summary>
    /// Save state: GP0/GP1 registers and the native 1024×512 VRAM. With the GL
    /// renderer the VRAM lives on the GPU (render targets written back first).
    /// </summary>
    internal void SaveState(BinaryWriter w)
    {
        w.Write(_drawAreaLeft); w.Write(_drawAreaTop); w.Write(_drawAreaRight); w.Write(_drawAreaBottom);
        w.Write(_drawOffsetX); w.Write(_drawOffsetY);
        w.Write(_texPageX); w.Write(_texPageY); w.Write(_texDepth); w.Write(_blendMode);
        w.Write(_dither); w.Write(_texDisable);
        w.Write(_texWinMaskX); w.Write(_texWinMaskY); w.Write(_texWinOffX); w.Write(_texWinOffY);
        w.Write(_setMask); w.Write(_checkMask);
        w.Write(_dispVramX); w.Write(_dispVramY);
        w.Write(_hRange1); w.Write(_hRange2); w.Write(_vRange1); w.Write(_vRange2);
        w.Write(_hres);
        w.Write(_hres368); w.Write(_vres480); w.Write(_pal); w.Write(_disp24); w.Write(_interlace);
        w.Write(_displayDisabled);
        w.Write(_dmaDir);
        w.Write(_gpuRead);
        w.Write(_statField);

        var pixels = new ushort[VramWidth * VramHeight];
        if (HleOn) GpuHle.Backend!.ReadVram(0, 0, VramWidth, VramHeight, pixels);
        else Vram.CopyTo(pixels, 0);
        w.Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
    }

    internal void LoadState(BinaryReader r)
    {
        _drawAreaLeft = r.ReadInt32(); _drawAreaTop = r.ReadInt32();
        _drawAreaRight = r.ReadInt32(); _drawAreaBottom = r.ReadInt32();
        _drawOffsetX = r.ReadInt32(); _drawOffsetY = r.ReadInt32();
        _texPageX = r.ReadInt32(); _texPageY = r.ReadInt32(); _texDepth = r.ReadInt32(); _blendMode = r.ReadInt32();
        _dither = r.ReadBoolean(); _texDisable = r.ReadBoolean();
        _texWinMaskX = r.ReadInt32(); _texWinMaskY = r.ReadInt32();
        _texWinOffX = r.ReadInt32(); _texWinOffY = r.ReadInt32();
        _setMask = r.ReadBoolean(); _checkMask = r.ReadBoolean();
        _dispVramX = r.ReadInt32(); _dispVramY = r.ReadInt32();
        _hRange1 = r.ReadInt32(); _hRange2 = r.ReadInt32(); _vRange1 = r.ReadInt32(); _vRange2 = r.ReadInt32();
        _hres = r.ReadInt32();
        _hres368 = r.ReadBoolean(); _vres480 = r.ReadBoolean(); _pal = r.ReadBoolean();
        _disp24 = r.ReadBoolean(); _interlace = r.ReadBoolean();
        _displayDisabled = r.ReadBoolean();
        _dmaDir = r.ReadInt32();
        _gpuRead = r.ReadUInt32();
        _statField = r.ReadBoolean();

        // Saved at a frame boundary: no command or image transfer is in flight.
        _fifo.Clear();
        _need = 0;
        _polyline = false;
        _loadImage = false;
        _readImage = false;
        _readUseBuf = false;
        _hleLoadActive = false;

        var pixels = new ushort[VramWidth * VramHeight];
        Host.SaveStates.ReadExactly(r, MemoryMarshal.AsBytes(pixels.AsSpan()));
        pixels.CopyTo(Vram, 0);
        if (HleOn) UploadChangedVram(pixels);

        GpuHle.NotifyDisplay(_dispVramX, _dispVramY, DisplayWidth, DisplayHeight);
    }

    /// <summary>
    /// Writes only the 64×64 tiles that differ. Textures the level already has
    /// stay as they are in the upscaled VRAM (replacement PNGs keep their detail).
    /// </summary>
    static void UploadChangedVram(ushort[] pixels)
    {
        var backend = GpuHle.Backend!;
        var current = new ushort[VramWidth * VramHeight];
        backend.ReadVram(0, 0, VramWidth, VramHeight, current);
        var tile = new ushort[StateTile * StateTile];
        for (int ty = 0; ty < VramHeight; ty += StateTile)
        for (int tx = 0; tx < VramWidth; tx += StateTile)
        {
            bool changed = false;
            for (int y = 0; y < StateTile && !changed; y++)
            {
                int row = (ty + y) * VramWidth + tx;
                changed = !pixels.AsSpan(row, StateTile).SequenceEqual(current.AsSpan(row, StateTile));
            }
            if (!changed) continue;
            for (int y = 0; y < StateTile; y++)
                pixels.AsSpan((ty + y) * VramWidth + tx, StateTile).CopyTo(tile.AsSpan(y * StateTile));
            backend.WriteVram(tx, ty, StateTile, StateTile, tile);
        }
    }
}
