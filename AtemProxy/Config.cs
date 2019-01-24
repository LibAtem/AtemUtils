using System.Collections.Generic;
using LibAtem.Common;

namespace AtemProxy
{
    public class Config
    {
        public string AtemAddress { get; set; }

        public Dictionary<MixEffectBlockId, MixEffectConfig> MixEffect { get; set; }

        public Dictionary<AuxiliaryId, Dictionary<char, VideoSource>> Auxiliary { get; set; }
        
        public Dictionary<SuperSourceBoxId, Dictionary<char, VideoSource>> SuperSource { get; set; }

        public class MixEffectConfig
        {
            public Dictionary<char, VideoSource> Program { get; set; }
            public Dictionary<char, VideoSource> Preview { get; set; }

            public char Cut { get; set; }
            public char Auto { get; set; }
        }
    }
}
