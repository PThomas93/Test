using Sharp7;
using System;
using System.IO;

namespace SerialPortReader.Feeders
{
    public class PLC : ControlFeeder, IDisposable
    {
        public const string Name = "PLC";

        private S7Client client;
        private byte[] dbWBuffer, dbRBuffer;
        private bool ReadInitialised = false;
        private bool WriteInitialised = false;
        private Func<uint> getFrameNumber;
        private StreamWriter dataOutFileR;
        private StreamWriter dataOutFileW;

        public class Data
        {
            public double rpmVal = 0;
            public double massVal = 0;
            public Data(double rpmVal = 0, double massVal = 0)
            {
                this.rpmVal = rpmVal;
                this.massVal = massVal;
            }
        }

        public enum VariablesIndex
        {
            RPM = 1,
            FeedRate = 2,
        }

        public PLC(string ip, string dataOutDir, Func<uint> getFrameNumber = null) : base()
        {
            var fileNameR = Path.GetFullPath(Path.Combine(dataOutDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_Read_" + Name + ".txt"));
            dataOutFileR = new StreamWriter(fileNameR);
            dataOutFile.WriteLine("DateTime" + "\t" + "FrameNumber" + "\t" + "RPM" + "\t" + "Mass");

            var fileNameW = Path.GetFullPath(Path.Combine(dataOutDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_Write_" + Name + ".txt"));
            dataOutFileW = new StreamWriter(fileNameW);
            dataOutFile.WriteLine("DateTime" + "\t" + "FrameNumber" + "\t" + "RPM" + "\t" + "Feeder");

            this.getFrameNumber = getFrameNumber;

            // Szótár létrehozása a StartPos hozzárendeléshez
            var MapStartPos = new Dictionary<int, int>();
            // Szótár létrehozása az OutputStyle hozzárendeléshez
            var MapOutputStyle = new Dictionary<int, string>();

            // StartPos hozzárendelése a változókhoz
            MapStartPos.Add(Convert.ToInt32(VariablesIndex.RPM), 0);
            MapStartPos.Add(Convert.ToInt32(VariablesIndex.FeedRate), 4);

            // OutputStyle hozzárendelése a változókhoz
            MapOutputStyle.Add(Convert.ToInt32(VariablesIndex.RPM), "\t");
            MapOutputStyle.Add(Convert.ToInt32(VariablesIndex.FeedRate), "\t\t\t");

            //-------------- Create and connect the client
            client = new S7Client();
            int result = client.ConnectTo(ip, 0, 1);
            if (result == 0)
            {
                Console.WriteLine("Connected to " + ip);
            }
            else
            {
                Console.WriteLine("Connection failed: " + client.ErrorText(result));
                return;
            }

            //-------------- Access DB_S7RemComm_Read
            Console.WriteLine("\n---- Accessing DB_S7RemComm_Read...");

            dbRBuffer = new byte[8];
            // Check whether the whole DB is accessible or not
            result = client.DBRead(23, 0, dbRBuffer.Length, dbRBuffer);

            if (result == 0)
            {
                Console.WriteLine("DB_S7RemComm_Read accessed.");
                ReadInitialised = true;
            }
            else
            {
                Console.WriteLine("Can't access DB_S7RemComm_Read: " + client.ErrorText(result));
                client.Disconnect();
                return;
            }

            //-------------- Access DB_S7RemComm_Write
            Console.WriteLine("\n---- Accessing DB_S7RemComm_Write...");

            dbWBuffer = new byte[8];
            // Check whether the whole DB is accessible or not
            result = client.DBRead(30, 0, dbWBuffer.Length, dbWBuffer);

            if (result == 0)
            {
                Console.WriteLine("DB_S7RemComm_Write accessed.");
                WriteInitialised = true;
            }
            else
            {
                Console.WriteLine("Can't access DB_S7RemComm_Write: " + client.ErrorText(result));
                client.Disconnect();
                return;
            }

        }

        public override object ReadFeeder()
        {
            if (ReadInitialised)
            {
                client.DBRead(23, 0, dbRBuffer.Length, dbRBuffer);

                // Actual RPM value measured by Motor Controller
                double rpmVal = S7.GetRealAt(dbRBuffer, 0);
                Console.WriteLine("Actual RPM value: " + rpmVal);

                // Actual Mass value of Material in Tank measured by PLC based on the scale's incoming data
                double massVal = S7.GetRealAt(dbRBuffer, 4);
                Console.WriteLine("Actual Mass value: " + massVal);

                //Write into file
                dataOutFileR.WriteLine(DateTime.Now + "\t" + getFrameNumber?.Invoke() + "\t" + rpmVal + "\t" + massVal);

                return new Data(rpmVal, massVal);
            }
            return new Data();
        }

        public override void WriteFeeder(double newVal, int index)
        {
            if (WriteInitialised)
            {
                // StartPos kiolvasása a bemeneten megadott indexű változóhoz
                int StartPos;
                MapStartPos.TryGetValue(index, out StartPos);

                // OutputStyle kiolvasása a bemeneten megadott indexű változóhoz
                string OutputStyle;
                MapOutputStyle.TryGetValue(index, out OutputStyle);

                S7.SetRealAt(dbWBuffer, StartPos, (float)newVal);
                int result = client.DBWrite(30, 0, dbWBuffer.Length, dbWBuffer);

                if (result != 0)
                {
                    Console.WriteLine("Error: " + client.ErrorText(result));
                }

                // Fájlba kiíratásnál, az oszlopok nevei a következőképp szerepelnek (egyelőre, ennyi változóval):
                // "DateTime" + "\t" + "FrameNumber" + "\t" + "RPM" + "\t" + "Feeder"
                // Ha RPM-et módosítunk: "FrameNumber" után 1 tabulátort, majd az RPM értéket írjuk
                // Ha FeedRate-t módosítunk: "FrameNumber" után 3 tabulátort (a 2. tabulátor helye az RPM oszlop, ezt átugorjuk, üresen hagyjuk), majd az RPM értéket írjuk
                dataOutFile.WriteLine(DateTime.Now + "\t" + getFrameNumber?.Invoke() + OutputStyle + newVal);
            }
        }

        public void Dispose()
        {
            if (initialised)
            {
                client.Disconnect();
                Console.WriteLine("\n---- Disconnecting...");
            }
        }
    }
}