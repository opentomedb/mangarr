using TagLib.Mpeg4;

namespace NzbDrone.Core.MediaFiles
{
    // TagLibSharp-Lidarr 2.2.0.19: UnknownBox reads its payload from wherever the file position
    // happens to be (no Seek to DataPosition), so Save() -- which re-renders the whole udta from
    // the parsed children -- writes every unmodelled udta child (the Nero chpl chapter list, ...)
    // back shifted. Reload those payloads from disk before saving.
    public class Mpeg4TagFile : TagLib.Mpeg4.File
    {
        public Mpeg4TagFile(string path)
            : base(path)
        {
        }

        public override void Save()
        {
            ReloadUnknownUdtaChildren();
            base.Save();
        }

        private void ReloadUnknownUdtaChildren()
        {
            Mode = AccessMode.Read;
            try
            {
                foreach (var udta in UdtaBoxes)
                {
                    // last element = the udta's own header; null / empty for a udta TagLib synthesised
                    var tree = udta.ParentTree;
                    if (tree == null || tree.Length == 0)
                    {
                        continue;
                    }

                    var udtaHeader = tree[tree.Length - 1];
                    var position = udtaHeader.Position + udtaHeader.HeaderSize;
                    var end = udtaHeader.Position + udtaHeader.TotalBoxSize;

                    foreach (var child in udta.Children)
                    {
                        if (position >= end)
                        {
                            break;
                        }

                        // the public ctor seeks and reads the header
                        var header = new BoxHeader(this, position);

                        // out of step with the parse: leave TagLib's view alone
                        if (header.TotalBoxSize == 0 || header.BoxType != child.BoxType)
                        {
                            break;
                        }

                        if (child is UnknownBox unknown)
                        {
                            Seek(header.Position + header.HeaderSize);
                            unknown.Data = ReadBlock((int)header.DataSize);
                        }

                        position += header.TotalBoxSize;
                    }
                }
            }
            finally
            {
                Mode = AccessMode.Closed;
            }
        }
    }
}
