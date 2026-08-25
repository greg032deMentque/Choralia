// Reflète ReorderSongsViewModel (back). L'ordre est déterminé par la position dans
// le tableau — le back rejette (409) si la liste n'est pas en Statut Draft, ou (400)
// si l'ensemble de SongIds ne correspond pas exactement à la composition actuelle.
export interface IReorderSongsRequest {
  SongIds: string[];
}
