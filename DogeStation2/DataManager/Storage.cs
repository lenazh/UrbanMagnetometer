﻿using System;
using System.IO;
using SystemWrapper.IO;
using SystemWrapper.Configuration;

// TODO: proper exception handling

namespace GDriveNURI
{
    public interface IStorage
    {
        void Store(double[] dataX, double[] dataY, double[] dataZ, double systemSeconds, 
            DateTime time);
        void Flush();
    }

    /* Interface for IBinaryWriterWrap object factory */
    public interface IBinaryWriterFactory
    {
        IBinaryWriterWrap Create(IFileStreamWrap stream);
    }

    /* Factory for IBinaryWriterWrap objects */
    public class BinaryWriterFactory : IBinaryWriterFactory
    {
        public IBinaryWriterWrap Create(IFileStreamWrap stream)
        {
            return new BinaryWriterWrap(stream);
        }
    }

    // TODO: scan the data cache folder for files that haven't been uploaded
    // and upload them as well
    class Storage : IStorage
    {
        private IUploadScheduler scheduler;
        private IBinaryWriterWrap x, y, z, t;
        public readonly IBinaryWriterFactory IBinaryWriter;
        public readonly IFileWrap IFile;
        public readonly IConfigurationManagerWrap ConfigurationManager;

        private string dataCacheFolder;
        private DatasetInfo info = null;
        private DatasetInfo postponedInfo = null;

        private bool isWriting = false;
        private long offset;

        /* Constructs the data writer given a Google Drive connection. */
        public Storage(IUploadScheduler scheduler)
        {
            this.scheduler = scheduler;
            IFile = new FileWrap();
            IBinaryWriter = new BinaryWriterFactory();
            ConfigurationManager = new ConfigurationManagerWrap();
            ReadAppConfig();
        }

        /* Constructs the object with custom filesystem wrappers. */
        public Storage(IUploadScheduler scheduler, IFileWrap file, 
            IBinaryWriterFactory binaryWriterFactory,
            IConfigurationManagerWrap configManager)
        {
            this.scheduler = scheduler;
            IFile = file;
            IBinaryWriter = binaryWriterFactory;
            ConfigurationManager = configManager;
            ReadAppConfig();
        }

        /* Stores the data from the sensor. */
        public void Store(double[] dataX, double[] dataY, double[] dataZ,
            double systemSeconds, DateTime time)
        {
            if (!isWriting)
            {
                CreateFiles(time);
            }

            if (info.StartDate.Hour != time.Hour)
            {
                CloseAndUploadAll(time);
                CreateFiles(time);
            }

            Append(dataX, dataY, dataZ, systemSeconds, time);
        }

        /* Forces the writer to send out the data. */
        public void Flush()
        {
            CloseAndUploadAll(DateTime.Now);
        }

        /* Initializes settings from the configuration file. */
        private void ReadAppConfig()
        {
            var settings = ConfigurationManager.AppSettings;
            dataCacheFolder = settings["DataCacheFolder"];
        }

        /* Appends the data into existing streams. */
        private void Append(double[] dataX, double[] dataY, double[] dataZ,
            double systemSeconds, DateTime time)
        {
            int length = dataX.Length;
            WriteArray(x, dataX);
            WriteArray(y, dataX);
            WriteArray(z, dataX);
            WriteTime(t, systemSeconds, time, offset, length);
            offset += length;
        }

        /* Writes an array of doubles into a binary file. */
        private static void WriteArray(IBinaryWriterWrap writer, double[] data)
        {
            foreach (double value in data)
            {
                writer.Write(value);
            }
        }

        /* Writes time data into a binary file. */
        private static void WriteTime(IBinaryWriterWrap writer, 
            double systemSeconds, DateTime time, long offset, int length)
        {
            writer.Write(offset);
            writer.Write(length);
            writer.Write(time.ToFileTimeUtc());
            writer.Write(systemSeconds);
        }

        /* Creates the data files in cache folder. */
        private void CreateFiles(DateTime time)
        {
            info = new DatasetInfo(time);
            x = new BinaryWriterWrap(File.Open(info.FullPath(info.XFileName),
                FileMode.Append, FileAccess.Write));
            y = new BinaryWriterWrap(File.Open(info.FullPath(info.YFileName),
                FileMode.Append, FileAccess.Write));
            z = new BinaryWriterWrap(File.Open(info.FullPath(info.ZFileName),
                FileMode.Append, FileAccess.Write));
            t = new BinaryWriterWrap(File.Open(info.FullPath(info.TFileName),
                FileMode.Append, FileAccess.Write));
            offset = 0;
            isWriting = true;
        }

        /* Closes the data files and sends them to the Google Drive.
        time indicates when the latest chunk of data was received. */
        private void CloseAndUploadAll(DateTime time)
        {
            x.Close();
            y.Close();
            z.Close();
            t.Close();

            /* Upload the latest postponed dataset, if any. */
            if ((postponedInfo != null) && !postponedInfo.isSameFile(info))
            {
                scheduler.UploadMagneticData(postponedInfo);
                postponedInfo = null;
            }

            if (!info.isSameFile(time))
            {
                scheduler.UploadMagneticData(info);
            }
            else
            {
                postponedInfo = info;
            }
        }
    }

}