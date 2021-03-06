﻿using FxSsh.Messages;
using FxSsh.Messages.Userauth;
using System;
using System.Diagnostics.Contracts;

namespace FxSsh.Services
{
    public class UserAuthService : SshService
    {
        public UserAuthService(Session session)
            : base(session)
        {
        }

        public event EventHandler<UserAuthArgs> UserAuth;

        public event EventHandler<string> Succeed;

        protected internal override void CloseService()
        {
        }

        internal void HandleMessageCore(UserauthServiceMessage message)
        {
            Contract.Requires(message != null);

            HandleMessage((dynamic)message);
        }

        private void HandleMessage(RequestMessage message)
        {
            switch (message.MethodName)
            {
                case "publickey":
                    {
                        var msg = Message.LoadFrom<PublicKeyRequestMessage>(message);
                        HandleMessage(msg);
                        break;
                    }

                case "password":
                    {
                        var msg = Message.LoadFrom<PasswordRequestMessage>(message);
                        HandleMessage(msg);
                        break;
                    }
                case "hostbased":
                case "none":
                default:
                    _session.SendMessage(new FailureMessage());
                    break;
            }
        }

        private void HandleMessage(PublicKeyRequestMessage message)
        {
            if (Session._publicKeyAlgorithms.ContainsKey(message.KeyAlgorithmName))
            {
                if (!message.HasSignature)
                {
                    _session.SendMessage(new PublicKeyOkMessage { KeyAlgorithmName = message.KeyAlgorithmName, PublicKey = message.PublicKey });
                    return;
                }

                var keyAlg = Session._publicKeyAlgorithms[message.KeyAlgorithmName](null);
                keyAlg.LoadKeyAndCertificatesData(message.PublicKey);

                var sig = keyAlg.GetSignature(message.Signature);
                var verifed = false;

                using (var worker = new SshDataWorker())
                {
                    worker.WriteBinary(_session.SessionId);
                    worker.Write(message.PayloadWithoutSignature);

                    verifed = keyAlg.VerifyData(worker.ToByteArray(), sig);
                }

                var args = new UserAuthArgs(message.KeyAlgorithmName, keyAlg.GetFingerprint(), message.PublicKey);
                if (verifed && UserAuth != null)
                {
                    UserAuth(this, args);
                    verifed = args.Result;
                }

                if (verifed)
                {
                    _session.RegisterService(message.ServiceName, args);
                    Succeed?.Invoke(this, message.ServiceName);
                    _session.SendMessage(new SuccessMessage());
                    return;
                }
                else
                {
                    _session.SendMessage(new FailureMessage());
                    throw new SshConnectionException("Authentication fail.", DisconnectReason.NoMoreAuthMethodsAvailable);
                }
            }
            _session.SendMessage(new FailureMessage());
        }

        private void HandleMessage(PasswordRequestMessage message)
        {
            var verifed = true;

            var args = new UserAuthArgs(message.Username, message.Password);
            if (verifed && UserAuth != null)
            {
                UserAuth(this, args);
                verifed = args.Result;
            }

            if (verifed)
            {
                _session.RegisterService(message.ServiceName, args);
                Succeed?.Invoke(this, message.ServiceName);
                _session.SendMessage(new SuccessMessage());
                return;
            }
            else
            {
                _session.SendMessage(new FailureMessage());
                throw new SshConnectionException("Authentication fail.", DisconnectReason.NoMoreAuthMethodsAvailable);
            }
        }
    }
}