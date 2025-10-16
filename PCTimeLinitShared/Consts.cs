using System;
using System.Collections.Generic;
using System.Text;

namespace PCTimeLinitShared
{
    static class Consts
    {
        public const string ServerIP = "10.0.0.7";
        public const int ServerPort = 8888;
        public const string AllowedUsageJsonExample = "{\n  \"monday\": [{ \"start\": \"08:00\", \"end\": \"15:00\" }],\n  \"tuesday\": [{ \"start\": \"08:00\", \"end\": \"15:00\" }],\n  \"wednesday\": [{ \"start\": \"08:00\", \"end\": \"15:00\" }],\n  \"thursday\": [{ \"start\": \"08:00\", \"end\": \"15:00\" }],\n  \"friday\": [{ \"start\": \"08:00\", \"end\": \"15:00\" }],\n  \"saturday\": [],\n  \"sunday\": []\n}";
    }
}
