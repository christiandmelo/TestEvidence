using System.Diagnostics;
using TestEvidence.Models;
using TestEvidence.Services;

namespace TestEvidence.UI;

public sealed class MainForm : Form
{
  private readonly SessaoRepository _repo = new();
  private ConfiguracaoApp _config = new();
  private bool _inicializado;

  private readonly Panel _faixa = new() { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(255, 244, 206), Visible = false };
  private readonly Label _lblFaixa = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
  private readonly Button _btnRetomarFaixa = new() { Text = "Retomar", Dock = DockStyle.Right, Width = 100 };
  private readonly ListView _lista = new()
  {
    Dock = DockStyle.Fill,
    View = View.Details,
    FullRowSelect = true,
    MultiSelect = false,
    HideSelection = false
  };
  private readonly Button _btnNova = new() { Text = "Nova evidência", Width = 130, Height = 30 };
  private readonly Button _btnConfig = new() { Text = "Configurações", Width = 110, Height = 30 };
  private readonly Button _btnRevisao = new() { Text = "Abrir revisão", AutoSize = true };
  private readonly Button _btnRetomar = new() { Text = "Retomar", AutoSize = true };
  private readonly Button _btnGerar = new() { Text = "Gerar documento", AutoSize = true };
  private readonly Button _btnAbrirDoc = new() { Text = "Abrir documento", AutoSize = true };
  private readonly Button _btnAbrirPasta = new() { Text = "Abrir pasta", AutoSize = true };
  private readonly Button _btnExcluir = new() { Text = "Excluir", AutoSize = true };
  private SessaoEvidencia? _sessaoEmAndamento;

  /// <summary>True enquanto existe uma evidência ativa (barra flutuante aberta) — usado pelo shell ao fechar a aba.</summary>
  public bool EmSessaoAtiva => ControladorSessao.Ativo != null;

  private IWin32Window Dono => (IWin32Window?)ParentForm ?? this;

  public MainForm()
  {
    Text = "Evidência de Teste";
    Icon = IconeApp.Obter();
    ClientSize = new Size(900, 560);
    MinimumSize = new Size(700, 400);
    StartPosition = FormStartPosition.CenterScreen;

    _lista.Columns.Add("Tarefa", 120);
    _lista.Columns.Add("Título", 280);
    _lista.Columns.Add("Início", 130);
    _lista.Columns.Add("Prints", 60);
    _lista.Columns.Add("Resultado", 100);
    _lista.Columns.Add("Status", 100);

    var topo = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6) };
    topo.Controls.AddRange(new Control[] { _btnNova, _btnConfig });

    _faixa.Controls.Add(_lblFaixa);
    _faixa.Controls.Add(_btnRetomarFaixa);

    var acoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
    acoes.Controls.AddRange(new Control[] { _btnRevisao, _btnRetomar, _btnGerar, _btnAbrirDoc, _btnAbrirPasta, _btnExcluir });

    var menu = new ContextMenuStrip();
    menu.Items.Add("Abrir revisão", null, (_, _) => AbrirRevisao());
    menu.Items.Add("Retomar", null, (_, _) => Retomar(Selecionada));
    menu.Items.Add("Gerar documento", null, (_, _) => GerarDocumento());
    menu.Items.Add("Abrir documento", null, (_, _) => AbrirDocumento());
    menu.Items.Add("Abrir pasta", null, (_, _) => AbrirPasta());
    menu.Items.Add("Excluir", null, (_, _) => Excluir());
    _lista.ContextMenuStrip = menu;

    Controls.Add(_lista);
    Controls.Add(acoes);
    Controls.Add(_faixa);
    Controls.Add(topo);

    _btnNova.Click += (_, _) => NovaEvidencia();
    _btnConfig.Click += (_, _) => { using var f = new ConfigForm(_config); f.ShowDialog(Dono); };
    _btnRetomarFaixa.Click += (_, _) => Retomar(_sessaoEmAndamento);
    _btnRevisao.Click += (_, _) => AbrirRevisao();
    _btnRetomar.Click += (_, _) => Retomar(Selecionada);
    _btnGerar.Click += (_, _) => GerarDocumento();
    _btnAbrirDoc.Click += (_, _) => AbrirDocumento();
    _btnAbrirPasta.Click += (_, _) => AbrirPasta();
    _btnExcluir.Click += (_, _) => Excluir();
    _lista.SelectedIndexChanged += (_, _) => AtualizarBotoes();
    _lista.DoubleClick += (_, _) => AbrirRevisao();

    ControladorSessao.EstadoMudou += AoEstadoMudar;
    Shown += (_, _) => Inicializar();
    VisibleChanged += (_, _) => { if (Visible) Inicializar(); };
    FormClosed += AoFechar;
  }

  // Sob o shell o Program.Main nunca roda: a inicialização vive aqui, de forma idempotente
  public void Inicializar()
  {
    if (_inicializado) return;
    _inicializado = true;
    try { Directory.CreateDirectory(CaminhoConfig.PastaSessoes); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    _config = ConfiguracaoApp.Carregar();
    Atualizar();
  }

  private void AoEstadoMudar(object? sender, EventArgs e)
  {
    if (IsDisposed || !IsHandleCreated) return;
    BeginInvoke(Atualizar);
  }

  private void AoFechar(object? sender, FormClosedEventArgs e)
  {
    ControladorSessao.EstadoMudou -= AoEstadoMudar;
    // fechar a aba não pode deixar a barra órfã: a sessão fica Pausada em disco
    ControladorSessao.Ativo?.Encerrar();
  }

  private SessaoEvidencia? Selecionada => _lista.SelectedItems.Count > 0 ? _lista.SelectedItems[0].Tag as SessaoEvidencia : null;

  private void Atualizar()
  {
    if (!_inicializado) return;
    var selecionadaId = Selecionada?.Id;
    var sessoes = _repo.Listar();

    _lista.BeginUpdate();
    _lista.Items.Clear();
    foreach (var s in sessoes)
    {
      var item = new ListViewItem(s.Tarefa) { Tag = s };
      item.SubItems.Add(s.Titulo);
      item.SubItems.Add(s.Inicio.ToString("dd/MM/yyyy HH:mm"));
      item.SubItems.Add(s.Evidencias.Count.ToString());
      item.SubItems.Add(s.Resultado switch
      {
        ResultadoTeste.Aprovado => "Aprovado",
        ResultadoTeste.Reprovado => "Reprovado",
        ResultadoTeste.ComRessalvas => "Com ressalvas",
        _ => "Pendente"
      });
      var ativa = ControladorSessao.Ativo?.Sessao.Id == s.Id;
      item.SubItems.Add(ativa ? "Em andamento" : s.Status switch
      {
        StatusSessao.EmAndamento => "Interrompida",
        StatusSessao.Pausada => "Pausada",
        _ => "Finalizada"
      });
      if (s.Id == selecionadaId) item.Selected = true;
      _lista.Items.Add(item);
    }
    _lista.EndUpdate();

    _sessaoEmAndamento = ControladorSessao.Ativo == null
      ? sessoes.FirstOrDefault(s => s.Status != StatusSessao.Finalizada)
      : null;
    _faixa.Visible = _sessaoEmAndamento != null;
    if (_sessaoEmAndamento != null)
      _lblFaixa.Text = $"Evidência em andamento: {_sessaoEmAndamento.Tarefa} — {_sessaoEmAndamento.Titulo}";
    AtualizarBotoes();
  }

  private void AtualizarBotoes()
  {
    var s = Selecionada;
    var temSelecao = s != null;
    var semAtiva = ControladorSessao.Ativo == null;
    _btnRevisao.Enabled = temSelecao && ControladorSessao.Ativo?.Sessao.Id != s!.Id;
    _btnRetomar.Enabled = temSelecao && semAtiva && s!.Status != StatusSessao.Finalizada;
    _btnGerar.Enabled = _btnRevisao.Enabled;
    _btnAbrirDoc.Enabled = temSelecao && !string.IsNullOrEmpty(s!.UltimoDocumentoGerado) && File.Exists(s.UltimoDocumentoGerado);
    _btnAbrirPasta.Enabled = temSelecao;
    _btnExcluir.Enabled = temSelecao && ControladorSessao.Ativo?.Sessao.Id != s!.Id;
    _btnNova.Enabled = semAtiva;
  }

  private Form? FormularioDono => ParentForm;

  private void NovaEvidencia()
  {
    using var form = new NovaSessaoForm(_config);
    if (form.ShowDialog(Dono) != DialogResult.OK) return;
    try
    {
      var sessao = _repo.Criar(form.Dados);
      ControladorSessao.Iniciar(sessao, _config, _repo, FormularioDono ?? this);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      MessageBox.Show(Dono, $"Não foi possível criar a sessão: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
  }

  private void Retomar(SessaoEvidencia? sessao)
  {
    if (sessao == null) return;
    var completa = _repo.Carregar(sessao.Id) ?? sessao;
    ControladorSessao.Iniciar(completa, _config, _repo, FormularioDono ?? this);
  }

  private void AbrirRevisao()
  {
    if (Selecionada is not { } s || ControladorSessao.Ativo?.Sessao.Id == s.Id) return;
    var completa = _repo.Carregar(s.Id) ?? s;
    new RevisaoForm(completa, _repo, _config, FormularioDono ?? this).Show();
  }

  private void GerarDocumento() => AbrirRevisao();

  private void AbrirDocumento()
  {
    if (Selecionada?.UltimoDocumentoGerado is { Length: > 0 } arq && File.Exists(arq))
      Abrir(arq);
  }

  private void AbrirPasta()
  {
    if (Selecionada is { } s)
      Abrir(_repo.PastaDa(s));
  }

  private static void Abrir(string caminho)
  {
    try { Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true }); }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException) { }
  }

  private void Excluir()
  {
    if (Selecionada is not { } s) return;
    if (MessageBox.Show(Dono, $"Excluir a evidência {s.Tarefa} e todos os prints? (vai para a Lixeira)", Text,
          MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
    try
    {
      _repo.ExcluirSessao(s.Id);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      MessageBox.Show(Dono, $"Não foi possível excluir: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    Atualizar();
  }
}
