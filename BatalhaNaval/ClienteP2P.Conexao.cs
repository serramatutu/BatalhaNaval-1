﻿using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Timers;

namespace BatalhaNaval
{
    /// <summary>
    /// Cliente Peer-to-Peer para a batalha naval
    /// </summary>
    public sealed partial class ClienteP2P
    {
        /// <summary>
        /// Porta onde o cliente procura conexões
        /// </summary>
        const int PortaTcp = 1337;

        /// <summary>
        /// Porta onde o servidor de broadcast procura conexões
        /// </summary>
        const int PortaBroadcast = 1729;

        /// <summary>
        /// Intervalo do timer sinalziador
        /// </summary>
        const double IntervaloSinalizador = 1000;

        // Servidor UDP para broadcasting, é isso que lista os IPs disponíveis para conexão
        // e responde requisições feitas por outros hosts para listar os computadores na rede
        UdpClient servidorBroadcast;

        // Servidor e clientes TCP para comunicação com os pontos remotos
        TcpListener servidor;
        TcpClient cliente;

        // Timer usado para sinalizar para os computadores da rede que você existe
        Timer sinalizador;

        // Tasks
        Task taskBroadcasting, taskConexao;

        /// <summary>
        /// Delegado de evento que recebe um endereço IP por parâmetro
        /// </summary>
        /// <param name="addr">Endereço IP passado para o evento</param>
        /// <returns>True ou False conforme necessário.</returns>
        public delegate bool EventoComEnderecoIP(IPAddress addr);

        /// <summary>
        /// Evento de cliente disponível detectado na rede. O retorno não é usado.
        /// </summary>
        public event EventoComEnderecoIP ClienteDisponivel;

        /// <summary>
        /// Evento de requisição de conexão com um cliente. 
        /// O retorno indica se a conexão deve ser aceita.
        /// </summary>
        public event EventoComEnderecoIP ClienteRequisitandoConexao;

        /// <summary>
        /// Evento de conexão bem sucedida com um cliente.
        /// O retorno não é usado.
        /// </summary>
        public event EventoComEnderecoIP ClienteConectado;

        /// <summary>
        /// Evento de falha na conexão com um cliente.
        /// O retorno não é usado.
        /// </summary>
        public event EventoComEnderecoIP ClienteDesconectado;

        /// <summary>
        /// Determina se o cliente está conectado a um cliente remoto
        /// </summary>
        public bool Conectado { get; private set; }

        /// <summary>
        /// Construtor
        /// </summary>
        private ClienteP2P()
        {
            servidorBroadcast = new UdpClient(new IPEndPoint(IPAddress.Broadcast, PortaBroadcast));
            servidorBroadcast.EnableBroadcast = true;
            servidorBroadcast.MulticastLoopback = false;

            servidor = new TcpListener(IPAddress.Any, PortaTcp);

            Conectado = false;
            sinalizador = new Timer(IntervaloSinalizador);
            sinalizador.Elapsed += (object sender, ElapsedEventArgs e) => SinalizarNaRede();
        }

        /// <summary>
        /// Conecta o cliente na rede
        /// </summary>
        public void Conectar()
        {
            servidor.Start();
            taskBroadcasting = Task.Run(() => TratarBroadcast());
            taskConexao = Task.Run(() => ResponderClientes());
            sinalizador.Start();
        }

        /// <summary>
        /// Solicita uma conexão com um cliente no IP remoto dado.
        /// Esse método trava a execução do programa enquanto espera pela resposta do cliente remoto
        /// </summary>
        /// <param name="ipRemoto">IP do cliente remoto</param>
        /// <returns>True caso a conexão seja bem sucedida e Falso caso contrário.</returns>
        public bool SolicitarConexao(IPAddress ipRemoto)
        {
            try
            {
                cliente = new TcpClient(new IPEndPoint(ipRemoto, PortaTcp));

                // Se recebeu um 0x34, a conexão deu certo
                if (cliente.GetStream().ReadByte() == 0x34)
                {
                    // Envia 0x69 para indicar reconhecimento da conexão
                    cliente.GetStream().WriteByte(0x69);

                    return true;
                }

                // Se não, deu ruim. Fecha o cliente.
                cliente.Close();

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Responde tentativas de conexão de clientes remotos
        /// </summary>
        private void ResponderClientes()
        {
            while (!Conectado)
            {
                cliente = servidor.AcceptTcpClient();
                IPAddress addr = (cliente.Client.RemoteEndPoint as IPEndPoint).Address;

                if (ClienteRequisitandoConexao(addr))
                {
                    try
                    {
                        // Envia um 0x34 para sinalizar que a conexão foi aceita
                        cliente.GetStream().WriteByte(0x34);

                        // Espera um 0x69 sinalizando que, realmente, a conexão deu certo
                        if (cliente.GetStream().ReadByte() == 0x69)
                        {
                            Conectado = true;
                            ClienteConectado(addr);
                        }
                    } catch {
                        ClienteDesconectado(addr);
                    }
                }
                else
                {
                    // Envia um 0x0 para sinalizar que a conexão foi rejeitada
                    cliente.GetStream().WriteByte(0x0);
                }
            }
        }

        /// <summary>
        /// Sinaliza para os outros clientes na rede que você existe
        /// </summary>
        private void SinalizarNaRede()
        {
            // Envia um 0 para todos os clientes na rede sinalizando que você existe
            servidorBroadcast.Send(new byte[] { 0 }, 1);
        }

        /// <summary>
        /// Thread de tratamento do broadcast
        /// </summary>
        private void TratarBroadcast()
        {
            try
            {
                while (!Conectado)
                {
                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = servidorBroadcast.Receive(ref endPoint);

                    if (!Conectado)
                        // Se recebeu dados, detectou um cliente na rede
                        ClienteDisponivel(endPoint.Address);
                }
            }
            catch (SocketException) {}
        }
    }
}