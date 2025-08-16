namespace PCTimeLinitShared.Messaging;

public enum MessageType
{
    CreateAccount = 1,
    Login = 2,
    Heartbeat = 3,
    RegisterComputer = 4,
    UpdateComputerStatus = 5,
    SetComputerTimeLimit = 6,
    GetComputersForAdmin = 7,
    ResetComputerTimer = 8,
    AcknowledgeReset = 9,
    Error = 99
}
