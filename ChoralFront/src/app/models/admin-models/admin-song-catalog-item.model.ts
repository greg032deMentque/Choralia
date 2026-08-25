// Reflète AdminSongCatalogItemViewModel (back, AdminSongController.GetPagedCatalogue) —
// catalogue transverse des chants (lot 4). Une ligne = un groupe d'AFFICHAGE calculé côté
// back (SongKeyHelper), jamais une ligne = un Song : le même titre déposé par 7 chorales
// ne doit apparaître qu'une seule fois. Aucune entité Oeuvre, aucune fusion, aucune écriture.
export interface IAdminSongCatalogItem {
  // Identifiant opaque de regroupement calculé par le back — jamais parsé ni reconstruit
  // côté front, transmis tel quel à GetGroupChoirs.
  Key: string;
  Title: string;
  // Un composer absent, nul ou vide signifie que ChantCleHelper n'a fusionné ce chant
  // avec aucun autre : le groupe est toujours réduit à lui seul (ChoirCount = 1). Ne
  // jamais fusionner deux titres identiques sans composer (ex. deux "Ave Maria").
  Composer: string | null;
  ChoirCount: number;
  // Peut différer de ChoirCount : une même chorale peut porter deux fois le même titre.
  OccurrenceCount: number;
}

// Reflète AdminSongCatalogPagedFilterViewModel — en complément de Filter (texte libre)
// porté par IPaginationQueryParams.
export interface IAdminSongCatalogFilter {
  DuplicatesOnly?: boolean;
}
