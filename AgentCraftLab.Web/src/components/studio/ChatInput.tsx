import { useState, useRef, useCallback, type KeyboardEvent, type ChangeEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Paperclip, Send, Square, X, FileText, FileSpreadsheet, File } from 'lucide-react'
import { api } from '@/lib/api'

export interface PendingFile {
  fileId: string
  fileName: string
  size: number
}

/** 共享 ref — ChatPanel 寫入，StableChatInput 讀取，避免 component identity 變化 */
export const chatInputFileRef: { current: ChatInputFileState } = {
  current: { pendingFile: null, onFileReady: () => {}, onFileRemove: () => {} },
}

interface ChatInputFileState {
  pendingFile: PendingFile | null
  onFileReady: (file: PendingFile) => void
  onFileRemove: () => void
}

/**
 * 穩定的 Input wrapper — module-level 定義，identity 永不變。
 * 透過 chatInputFileRef 讀取最新的附件狀態。
 */
export function StableChatInput(props: {
  inProgress: boolean
  onSend: (text: string) => Promise<unknown>
  isVisible?: boolean
  onStop?: () => void
  chatReady?: boolean
  hideStopButton?: boolean
}) {
  const { pendingFile, onFileReady, onFileRemove } = chatInputFileRef.current
  return (
    <ChatInput
      {...props}
      pendingFile={pendingFile}
      onFileReady={onFileReady}
      onFileRemove={onFileRemove}
    />
  )
}

interface ChatInputProps {
  inProgress: boolean
  onSend: (text: string) => Promise<unknown>
  isVisible?: boolean
  onStop?: () => void
  chatReady?: boolean
  hideStopButton?: boolean
  pendingFile: PendingFile | null
  onFileReady: (file: PendingFile) => void
  onFileRemove: () => void
}

const MAX_FILE_SIZE = 32 * 1024 * 1024 // 32 MB

const FILE_ACCEPT = '.pdf,.docx,.pptx,.html,.txt,.md,.csv,.json,.xml,.yaml,.yml'

function getFileIcon(fileName: string) {
  const ext = fileName.split('.').pop()?.toLowerCase() ?? ''
  if (['csv', 'xlsx', 'xls', 'tsv'].includes(ext)) return FileSpreadsheet
  if (['pdf', 'docx', 'doc', 'pptx', 'ppt', 'txt', 'md', 'html'].includes(ext)) return FileText
  return File
}

function FileIconFor({ fileName }: { fileName: string }) {
  const Icon = getFileIcon(fileName)
  return <Icon size={14} className="chat-file-icon" />
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function ChatInput({
  inProgress,
  onSend,
  onStop,
  chatReady,
  hideStopButton,
  pendingFile,
  onFileReady,
  onFileRemove,
}: ChatInputProps) {
  const { t } = useTranslation('chat')
  const [text, setText] = useState('')
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const canSend = !inProgress && !uploading && text.trim().length > 0

  const handleSend = useCallback(async () => {
    if (!canSend) return
    const msg = text.trim()
    setText('')
    if (textareaRef.current) textareaRef.current.style.height = 'auto'
    await onSend(msg)
  }, [canSend, text, onSend])

  const handleKeyDown = useCallback((e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      handleSend()
    }
  }, [handleSend])

  const handleTextChange = useCallback((e: ChangeEvent<HTMLTextAreaElement>) => {
    setText(e.target.value)
    // auto-resize up to 6 rows
    const el = e.target
    el.style.height = 'auto'
    const lineHeight = 24
    el.style.height = `${Math.min(el.scrollHeight, lineHeight * 6)}px`
  }, [])

  const handleFileSelect = useCallback(async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (!file) return

    setUploadError('')

    if (file.size > MAX_FILE_SIZE) {
      setUploadError(t('attachment.tooLarge', { max: '32 MB' }))
      return
    }

    setUploading(true)
    try {
      const result = await api.upload.file(file)
      onFileReady(result)
    } catch {
      setUploadError(t('attachment.uploadFailed'))
    } finally {
      setUploading(false)
    }
  }, [t, onFileReady])

  const handleRemoveFile = useCallback(() => {
    setUploadError('')
    onFileRemove()
  }, [onFileRemove])

  // Button state machine: !chatReady → spinner, inProgress → stop, else → send
  const showStop = chatReady !== false && inProgress && !hideStopButton

  return (
    <div className="copilotKitInputContainer">
      <div className="copilotKitInput">
        {/* File preview badge */}
        {(pendingFile || uploading) && (
          <div className="chat-file-preview">
            {uploading ? (
              <div className="chat-file-badge uploading">
                <span className="chat-file-spinner" />
                <span className="chat-file-name">{t('attachment.uploading')}</span>
              </div>
            ) : pendingFile ? (
              <div className="chat-file-badge">
                <FileIconFor fileName={pendingFile.fileName} />
                <span className="chat-file-name">{pendingFile.fileName}</span>
                <span className="chat-file-size">{formatSize(pendingFile.size)}</span>
                <button
                  className="chat-file-remove"
                  onClick={handleRemoveFile}
                  title={t('attachment.remove')}
                >
                  <X size={12} />
                </button>
              </div>
            ) : null}
          </div>
        )}

        {/* Upload error */}
        {uploadError && (
          <div className="chat-upload-error">{uploadError}</div>
        )}

        {/* Textarea */}
        <textarea
          ref={textareaRef}
          value={text}
          onChange={handleTextChange}
          onKeyDown={handleKeyDown}
          placeholder={t('placeholder')}
          disabled={inProgress}
          rows={1}
          style={{ resize: 'none' }}
        />

        {/* Controls */}
        <div className="copilotKitInputControls">
          {/* Attach button */}
          <button
            className={`copilotKitInputControlButton${pendingFile ? ' has-file' : ''}`}
            onClick={() => fileInputRef.current?.click()}
            disabled={inProgress || uploading}
            title={t('attachment.attach')}
          >
            <Paperclip size={16} />
          </button>
          <input
            ref={fileInputRef}
            type="file"
            accept={FILE_ACCEPT}
            className="hidden"
            onChange={handleFileSelect}
          />

          <div style={{ flexGrow: 1 }} />

          {/* Send / Stop button */}
          {showStop ? (
            <button
              className="copilotKitInputControlButton"
              onClick={onStop}
              title={t('stop')}
            >
              <Square size={14} />
            </button>
          ) : (
            <button
              className="copilotKitInputControlButton"
              onClick={handleSend}
              disabled={!canSend}
              title={t('attachment.send', 'Send')}
            >
              {chatReady === false ? (
                <span className="chat-file-spinner" />
              ) : (
                <Send size={14} />
              )}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
