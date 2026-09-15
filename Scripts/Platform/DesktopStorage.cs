using System.Security.Cryptography;
using System.Text;
using DiceGame.App;
using DiceGame.Core;
using Godot;

namespace DiceGame.Platform;

public sealed class DesktopStorage(GameData data) : IDesktopStorage
{
    private readonly string _path=ProjectSettings.GlobalizePath("user://dice_ricochet_save_v1.json");
    public string? Read()
    {
        bool found = false;
        foreach(string path in new[]{_path,_path+".bak"})
        {
            if(!System.IO.File.Exists(path))continue;
            found = true;
            try
            {
                string text=System.IO.File.ReadAllText(path,Encoding.UTF8);SaveCodec.Decode(data,text);return text;
            }
            catch(Exception e){GD.PushWarning("Ignored damaged save: "+path+" ("+e.Message+")");}
        }
        if(found) throw new InvalidDataException("主存档与备份均无法校验。请先导出备份，再明确选择重置。");
        return null;
    }
    public void Write(string json)
    {
        string dir=System.IO.Path.GetDirectoryName(_path)!;Directory.CreateDirectory(dir);
        string temp=_path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,System.IO.FileAccess.Write,FileShare.None))
        {
            byte[] bytes=Encoding.UTF8.GetBytes(json);stream.Write(bytes);stream.Flush(true);
        }
        if(System.IO.File.Exists(_path))
        {
            // Preserve a verified previous generation. Rename/replace keeps readers from seeing a partial JSON.
            try{SaveCodec.Decode(data,System.IO.File.ReadAllText(_path));System.IO.File.Copy(_path,_path+".bak",true);}
            catch(System.Text.Json.JsonException){}catch(InvalidDataException){}
        }
        System.IO.File.Move(temp,_path,true);
    }
    public string DirectoryPath => System.IO.Path.GetDirectoryName(_path)!;
    public string Backup()
    {
        Directory.CreateDirectory(DirectoryPath);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        string folder = System.IO.Path.Combine(DirectoryPath, "backups", stamp); Directory.CreateDirectory(folder);
        bool copied = false;
        foreach (string source in new[] { _path, _path + ".bak" })
            if (System.IO.File.Exists(source))
            {
                string target=System.IO.Path.Combine(folder,System.IO.Path.GetFileName(source));
                using(var input=System.IO.File.OpenRead(source)) using(var output=new FileStream(target,FileMode.CreateNew,System.IO.FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
                using var original=System.IO.File.OpenRead(source); using var backup=System.IO.File.OpenRead(target);
                if(!SHA256.HashData(original).SequenceEqual(SHA256.HashData(backup))) throw new IOException("备份校验失败。"); copied=true;
            }
        if(!copied) throw new InvalidOperationException("尚无存档文件可备份。");
        return folder;
    }
    public void ArchiveAndReset()
    {
        if (System.IO.File.Exists(_path) || System.IO.File.Exists(_path + ".bak")) Backup();
        // A verified copy is made first; no historical backup directory is ever removed.
        foreach (string source in new[] { _path, _path + ".bak", _path + ".tmp" }) if(System.IO.File.Exists(source)) System.IO.File.Delete(source);
    }
    public uint NewSeed()
    {
        Span<byte> bytes=stackalloc byte[4];System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);uint seed=BitConverter.ToUInt32(bytes);return seed==0?1u:seed;
    }
}
