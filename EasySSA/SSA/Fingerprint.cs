﻿#region License
// ====================================================
// EasySSA Copyright(C) 2017 Furkan Türkal
// This program comes with ABSOLUTELY NO WARRANTY; This is free software,
// and you are welcome to redistribute it under certain conditions; See
// file LICENSE, which is part of this source code package, for details.
// ====================================================
#endregion

using EasySSA.Core.Network.Securities;

namespace EasySSA.SSA {
    public sealed class Fingerprint {
        
        public string IdentityID { get; private set; }

        public byte IdentityFlag { get; private set; }

        public SecurityFlags SecurityFlag { get; private set; }

        public string SHA { get; private set; }

        public Fingerprint(string identityID, byte identityFlag, SecurityFlags securityFlag, string sha) {
            this.IdentityID = identityID;
            this.IdentityFlag = identityFlag;
            this.SecurityFlag = securityFlag;
            this.SHA = sha;
        }
    }
}
